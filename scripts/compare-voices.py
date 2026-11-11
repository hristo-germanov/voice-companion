"""Generate the same synthetic English text with local XTTS and Kokoro voices."""
import argparse
import gc
import json
import os
from pathlib import Path
import time

parser = argparse.ArgumentParser()
parser.add_argument('--engine', choices=['xtts', 'kokoro'], required=True)
parser.add_argument('--accept-license', action='store_true')
args = parser.parse_args()
if args.engine == 'xtts' and not args.accept_license:
    parser.error('XTTS requires prior acceptance of its CPML license; pass --accept-license.')

root = Path(__file__).resolve().parents[1]
output = root / 'artifacts/coqui-benchmark'
output.mkdir(parents=True, exist_ok=True)
os.environ['HF_HOME'] = str(root / 'artifacts/coqui-hf-cache')
text = ('Good morning. Here is a quick update on our project. The new release is ready for review, '
        'and the team will meet tomorrow to discuss the next steps.')
manifest_path = output / 'comparison.json'
data = json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.exists() else {'text': text, 'samples': []}
assert data['text'] == text, 'Use a separate output manifest when changing the comparison text.'

import torch
import soundfile as sf
torch.set_num_threads(4)
device = 'cuda' if torch.cuda.is_available() else 'cpu'
data['device'] = device
data['gpu'] = torch.cuda.get_device_name(0) if device == 'cuda' else None

def measure(name, voice, stream):
    torch.manual_seed(42)
    if device == 'cuda':
        torch.cuda.synchronize()
        torch.cuda.reset_peak_memory_stats()
    start = time.perf_counter()
    first = None
    chunks = []
    for audio in stream(text):
        audio = audio.detach().cpu().flatten()
        if not audio.numel(): continue
        if first is None: first = time.perf_counter() - start
        chunks.append(audio)
    if not chunks: raise RuntimeError(f'No audio for {voice}')
    elapsed = time.perf_counter() - start
    audio = torch.cat(chunks).numpy()
    duration = len(audio) / 24000
    sf.write(str(output / (name + '.wav')), audio, 24000, subtype='PCM_16')
    sample = {'name': name, 'engine': args.engine, 'voice': voice, 'first_audio_seconds': round(first, 3),
              'generation_seconds': round(elapsed, 3), 'audio_seconds': round(duration, 3),
              'real_time_factor': round(elapsed / duration, 3),
              'peak_gpu_memory_mb': round(torch.cuda.max_memory_allocated() / 1024 ** 2) if device == 'cuda' else None}
    data['samples'] = [s for s in data['samples'] if s['name'] != name] + [sample]
    temporary = manifest_path.with_suffix('.tmp')
    temporary.write_text(json.dumps(data, indent=2), encoding='utf-8')
    temporary.replace(manifest_path)
    print(json.dumps(sample), flush=True)

if args.engine == 'xtts':
    import pysbd
    from TTS.tts.configs.xtts_config import XttsConfig
    from TTS.tts.models.xtts import Xtts
    config = XttsConfig()
    model_dir = root / 'artifacts/coqui-model-files'
    config.load_json(str(model_dir / 'config.json'))
    start = time.perf_counter()
    model = Xtts(config)
    model.load_checkpoint(config, checkpoint_dir=str(model_dir), use_deepspeed=False)
    model = model.to(device)
    print(f'XTTS loaded in {time.perf_counter() - start:.2f}s', flush=True)
    segmenter = pysbd.Segmenter(language='en', clean=False)
    for voice in ['Ana Florence', 'Daisy Studious', 'Andrew Chipper', 'Craig Gutsy']:
        latent, embedding = model.speaker_manager.speakers[voice].values()
        def stream(value):
            for sentence in segmenter.segment(value):
                yield from model.inference_stream(sentence, 'en', latent, embedding, enable_text_splitting=False)
        list(stream('Good morning.'))  # Warm up each voice, outside the measured request.
        measure('xtts-' + voice.lower().replace(' ', '-'), voice, stream)
else:
    from kokoro import KPipeline, KModel
    start = time.perf_counter()
    model_dir = root / 'artifacts/kokoro-model-files'
    model = KModel(repo_id='hexgrad/Kokoro-82M', config=str(model_dir / 'config.json'),
                   model=str(model_dir / 'kokoro-v1_0.pth')).to(device).eval()
    pipeline = KPipeline(lang_code='a', repo_id='hexgrad/Kokoro-82M', model=model)
    british = KPipeline(lang_code='b', repo_id='hexgrad/Kokoro-82M', model=pipeline.model)
    print(f'Kokoro initialized in {time.perf_counter() - start:.2f}s (may include first download)', flush=True)
    for voice in ['af_heart', 'af_bella', 'am_michael', 'bm_george']:
        selected = british if voice.startswith('b') else pipeline
        voice_path = str(model_dir / (voice + '.pt'))
        selected.load_voice(voice_path)  # Exclude voice loading from synthesis timing.
        def stream(value):
            for _, _, audio in selected(value, voice=voice_path, speed=1):
                if audio is not None: yield audio
        list(stream('Good morning.'))
        measure('kokoro-' + voice, voice, stream)

print('Comparison complete:', args.engine, flush=True)
