"""Kokoro protocol stub that withholds later audio during the startup check."""

import base64
from http.server import BaseHTTPRequestHandler, HTTPServer
import io
import json
import os
from pathlib import Path
import sys
import time
import wave


class Handler(BaseHTTPRequestHandler):
    requested_first = False

    def do_POST(self):
        if self.path != '/synthesize' or self.headers.get('X-Kokoro-Token') != os.environ['KOKORO_TOKEN']:
            self.send_error(403)
            return
        request = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        if not Handler.requested_first:
            Handler.requested_first = True
            if len(request['text']) > 400:
                self.send_error(422, 'First chunk exceeded 400 characters')
                return
        else:
            Path('later-part-requested').touch()
            deadline = time.monotonic() + 15
            while not Path('release-later-part').exists():
                if time.monotonic() >= deadline:
                    self.send_error(504, 'Later chunk was never released')
                    return
                time.sleep(0.025)

        audio = io.BytesIO()
        with wave.open(audio, 'wb') as pcm:
            pcm.setparams((1, 2, 24000, 0, 'NONE', 'not compressed'))
            pcm.writeframes(bytes(24000 * 2 * 3))
        response = json.dumps({'audio': base64.b64encode(audio.getvalue()).decode('ascii'),
                               'durationSeconds': 3, 'words': []}).encode('utf-8')
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(response)))
        self.end_headers()
        self.wfile.write(response)


if __name__ == '__main__':
    with HTTPServer(('127.0.0.1', int(sys.argv[1])), Handler) as server:
        print('READY', flush=True)
        server.serve_forever()
