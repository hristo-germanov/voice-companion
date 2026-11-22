"""Opt-in playback regression check for Windows with a working audio output."""

import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
from urllib.error import URLError
from urllib.request import ProxyHandler, Request, build_opener


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_API = ROOT / 'apps/api/bin/Debug/net8.0/ChatGPTVoiceCompanion.Api.dll'
STARTUP_BUDGET = 2.0
BUFFERING_DEADLINE = 4.0
PASSAGE = ('The morning train rolled into the station just as the rain began. '
           'Travelers folded their umbrellas and gathered their bags. '
           'Across the platform, a station worker checked the timetable and waved to the driver. ')
TEXT = ' '.join((PASSAGE * 25).split()[:650]).rstrip('.') + '.'


def wait_for(check, deadline, failure):
    while (remaining := deadline - time.monotonic()) > 0:
        if check(min(remaining, 1.0)) and time.monotonic() <= deadline:
            return
        time.sleep(min(0.05, max(0, deadline - time.monotonic())))
    raise AssertionError(failure)


def check_startup(api_dll, directory, real_voice):
    with socket.socket() as api_socket, socket.socket() as worker_socket:
        api_socket.bind(('127.0.0.1', 0))
        worker_socket.bind(('127.0.0.1', 0))
        api_port = api_socket.getsockname()[1]
        worker_port = worker_socket.getsockname()[1]
    base = f'http://127.0.0.1:{api_port}'
    opener = build_opener(ProxyHandler({}))

    def api(path, timeout, body=None):
        data = None if body is None else json.dumps(body).encode('utf-8')
        request = Request(base + '/api/' + path, data=data, headers={'Content-Type': 'application/json'})
        with opener.open(request, timeout=timeout) as response:
            return json.load(response)

    python = ROOT / 'artifacts/coqui-venv/Scripts/python.exe' if real_voice else sys.executable
    worker = ROOT / 'scripts/kokoro-worker.py' if real_voice else ROOT / 'tests/playback_startup_worker.py'
    command = ['dotnet', str(api_dll), f'--Kestrel:Endpoints:Http:Url={base}',
               '--ConnectionStrings:AppDb=Data Source=playback-test.db', '--Speech:Mode=kokoro',
               '--Speech:KokoroVoice=af_heart', '--Speech:KokoroSpeed=1',
               f'--Speech:KokoroPort={worker_port}', f'--Speech:KokoroPythonPath={python}',
               f'--Speech:KokoroWorkerPath={worker}', '--Logging:LogLevel:Default=Warning']
    log_path = directory / 'api.log'
    with log_path.open('w', encoding='utf-8') as log:
        process = subprocess.Popen(command, cwd=directory, stdout=log, stderr=subprocess.STDOUT,
                                   env={**os.environ, 'TEMP': str(directory), 'TMP': str(directory)})
        try:
            def healthy(timeout):
                if process.poll() is not None:
                    raise AssertionError(f'Test API exited with code {process.returncode}.')
                try:
                    return api('health', timeout)['status'] == 'ok'
                except (URLError, TimeoutError):
                    return False

            def speech_event(message_id, timeout):
                event = api('speech/diagnostics', timeout)['latestEvent']
                if event is None or event['messageId'] != message_id:
                    return None
                if event['status'] in ('failed', 'skipped'):
                    raise AssertionError(f"Playback {event['status']}: {event['error']}")
                return event['status']

            wait_for(healthy, time.monotonic() + 30, 'The isolated test API did not start.')
            if real_voice:
                warmup = api('speech/text', 5, {'text': 'Warming up the playback device.'})['messageId']
                wait_for(lambda timeout: speech_event(warmup, timeout) == 'completed',
                         time.monotonic() + 100, 'Kokoro and the audio device did not warm up.')
                wait_for(lambda timeout: api('speech/tracker', timeout)['currentMessageId'] is None,
                         time.monotonic() + 5, 'Warm-up did not leave the playback queue idle.')

            budget = STARTUP_BUDGET if real_voice else BUFFERING_DEADLINE
            started = time.monotonic()
            message_id = api('speech/text', budget, {'text': TEXT})['messageId']

            def first_audio(timeout):
                tracker = api('speech/tracker', timeout)
                if tracker['currentMessageId'] != str(message_id) or tracker['positionSeconds'] <= 0:
                    return False
                if real_voice:
                    return True
                # The worker leaves the second chunk unfinished until after the assertion.
                return (tracker['currentSegmentIndex'] == 1 and tracker['totalSegments'] > 1
                        and not tracker['isFullyBuffered'] and (directory / 'later-part-requested').exists())

            expectation = 'while a later chunk was generating' if not real_voice else 'with warmed Kokoro'
            try:
                wait_for(first_audio, started + budget,
                         f'{len(TEXT.split())}-word passage did not start within {budget:.1f}s {expectation}.')
            except AssertionError:
                speech_event(message_id, 1)
                raise
            elapsed = time.monotonic() - started
            if not real_voice:
                (directory / 'release-later-part').touch()
            print(f'Playback started in {elapsed:.3f}s {expectation} ({len(TEXT.split())} words).')
        finally:
            if process.poll() is None:
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'],
                               stdout=subprocess.DEVNULL, check=True, timeout=10)
                process.wait(timeout=5)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--api-dll', type=Path, default=DEFAULT_API, help='Path to a freshly built API DLL.')
    parser.add_argument('--real-voice', action='store_true', help='Measure startup using the local Kokoro model.')
    args = parser.parse_args()
    if sys.platform != 'win32':
        parser.error('The playback check requires Windows audio.')
    if not args.api_dll.is_file():
        parser.error(f'Build the API first. Missing file: {args.api_dll}')

    temporary = tempfile.TemporaryDirectory(prefix='playback-startup-')
    directory = Path(temporary.name)
    try:
        check_startup(args.api_dll.resolve(), directory, args.real_voice)
    except (AssertionError, URLError, TimeoutError, subprocess.SubprocessError) as error:
        print(f'FAIL: {error}', file=sys.stderr)
        log_path = directory / 'api.log'
        if log_path.is_file():
            print(log_path.read_text(encoding='utf-8', errors='replace'), file=sys.stderr)
        return 1
    finally:
        # Windows can briefly keep the worker's loaded libraries locked after taskkill.
        deadline = time.monotonic() + 5
        while True:
            try:
                temporary.cleanup()
                break
            except OSError as error:
                if error.winerror not in (5, 32, 267) or time.monotonic() >= deadline:
                    raise
                time.sleep(0.1)
    return 0


if __name__ == '__main__':
    sys.exit(main())
