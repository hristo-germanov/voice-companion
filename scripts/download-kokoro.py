"""Download the Kokoro model and the two supported voices into ignored artifacts/."""
from pathlib import Path
import urllib.request

folder = Path(__file__).resolve().parents[1] / 'artifacts/kokoro-model-files'
folder.mkdir(parents=True, exist_ok=True)
for name in ['config.json', 'kokoro-v1_0.pth', 'voices/af_heart.pt', 'voices/af_bella.pt']:
    path = folder / name.split('/')[-1]
    if path.exists() and path.stat().st_size:
        continue
    print('Downloading', name, flush=True)
    temporary = path.with_suffix(path.suffix + '.partial')
    with urllib.request.urlopen(f'https://huggingface.co/hexgrad/Kokoro-82M/resolve/main/{name}', timeout=60) as response:
        with temporary.open('wb') as output:
            while data := response.read(1024 * 1024): output.write(data)
    temporary.replace(path)
