"""Resident loopback-only Kokoro service used by the .NET speech queue."""
import contextlib
import base64
import io
import json
import os
from pathlib import Path
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from kokoro_alignment import align_words

root = Path(__file__).resolve().parents[1]
os.environ['HF_HOME'] = str(root / 'artifacts/coqui-hf-cache')
os.environ['HF_HUB_OFFLINE'] = '1'
token = os.environ['KOKORO_TOKEN']
with contextlib.redirect_stdout(sys.stderr):
    import torch
    import soundfile as sf
    from kokoro import KModel, KPipeline
    torch.set_num_threads(4)
    model_dir = root / 'artifacts/kokoro-model-files'
    model = KModel(repo_id='hexgrad/Kokoro-82M', config=str(model_dir / 'config.json'),
                   model=str(model_dir / 'kokoro-v1_0.pth')).to('cuda' if torch.cuda.is_available() else 'cpu').eval()
    pipeline = KPipeline(lang_code='a', model=model, repo_id='hexgrad/Kokoro-82M')
    voices = {name: str(model_dir / (name + '.pt')) for name in ['af_heart', 'af_bella']}
    for path in voices.values():
        pipeline.load_voice(path)
        list(pipeline('Ready to read.', voice=path))

generation_lock = threading.Lock()

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass  # Never log submitted text.

    def do_POST(self):
        if self.path != '/synthesize' or self.headers.get('X-Kokoro-Token') != token:
            self.send_error(403)
            return
        try:
            length = int(self.headers.get('Content-Length', 0))
            if not 0 < length <= 250000: raise ValueError('Invalid request size')
            request = json.loads(self.rfile.read(length))
            text = request.get('text', '')
            voice = request.get('voice', 'af_heart')
            speed = float(request.get('speed', 1))
            if voice not in voices or not isinstance(text, str) or not text.strip() or len(text) > 50000 or not 0.5 <= speed <= 2:
                raise ValueError('Invalid text, voice or speed')
            with generation_lock, contextlib.redirect_stdout(sys.stderr):
                chunks, words, cursor, time_offset = [], [], 0, 0
                for part in pipeline(text, voice=voices[voice], speed=speed):
                    if part.audio is None:
                        continue
                    aligned, cursor = align_words(text, part.tokens, cursor, time_offset)
                    words.extend(aligned)
                    chunks.append(part.audio)
                    time_offset += part.audio.numel() / 24000
                if not chunks: raise ValueError('No readable audio was generated')
                audio = torch.cat(chunks).detach().cpu().numpy()
                buffer = io.BytesIO()
                sf.write(buffer, audio, 24000, format='WAV', subtype='PCM_16')
                result = json.dumps({'audio': base64.b64encode(buffer.getvalue()).decode('ascii'),
                                     'durationSeconds': time_offset, 'words': words}).encode('utf-8')
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(result)))
            self.end_headers()
            self.wfile.write(result)
        except (BrokenPipeError, ConnectionResetError):
            pass  # Stop canceled the request; the loaded model can serve the next one.
        except ValueError as error:
            self.send_error(400, str(error))
        except Exception:
            self.send_error(500, 'Kokoro generation failed')

server = ThreadingHTTPServer(('127.0.0.1', int(sys.argv[1])), Handler)
server.daemon_threads = True
print('READY', flush=True)
server.serve_forever()
