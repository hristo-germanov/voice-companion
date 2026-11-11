"""Local XTTS-v2 evaluation with built-in speaker and synthetic English text.

Run from the repository root using artifacts/coqui-venv/Scripts/python.exe.
Requires --accept-license after reviewing the XTTS-v2 CPML license.
All downloads and generated audio stay under ignored artifacts/.
"""
import argparse
import json
import os
from pathlib import Path
import time

parser = argparse.ArgumentParser()
parser.add_argument('--accept-license', action='store_true')
args = parser.parse_args()
if not args.accept_license:
    parser.error('Review https://huggingface.co/coqui/XTTS-v2/blob/main/LICENSE.txt and pass --accept-license.')

root = Path(__file__).resolve().parents[1]
output = root / 'artifacts' / 'coqui-benchmark'
output.mkdir(parents=True, exist_ok=True)
(output / 'index.html').write_text((root / 'scripts' / 'coqui-preview.html').read_text(encoding='utf-8'), encoding='utf-8')
os.environ['TTS_HOME'] = str(root / 'artifacts' / 'coqui-models')
os.environ['HF_HOME'] = str(root / 'artifacts' / 'coqui-hf-cache')
os.environ['COQUI_TOS_AGREED'] = '1'

import torch
import soundfile as sf
import pysbd
from TTS.tts.configs.xtts_config import XttsConfig
from TTS.tts.models.xtts import Xtts

device = 'cuda' if torch.cuda.is_available() else 'cpu'
torch.set_num_threads(4)
print(f'Device: {device}', flush=True)
if device == 'cuda':
    print(f'GPU: {torch.cuda.get_device_name(0)}', flush=True)
start = time.perf_counter()
model_dir = root / 'artifacts' / 'coqui-model-files'
config = XttsConfig()
config.load_json(str(model_dir / 'config.json'))
model = Xtts(config)
model.load_checkpoint(config, checkpoint_dir=str(model_dir), use_deepspeed=False)
model = model.to(device)
load_seconds = time.perf_counter() - start
speakers = model.speaker_manager.speakers
speaker = 'Ana Florence' if 'Ana Florence' in speakers else next(iter(speakers))
gpt_latent, embedding = speakers[speaker].values()
print(f'Model loaded in {load_seconds:.2f}s; speaker: {speaker}', flush=True)

samples = [
    ('short-cold', 'Hello! This voice is generated locally on this computer. No online speech service is used.'),
    ('short-warm', 'Hello! This voice is generated locally on this computer. No online speech service is used.'),
    ('paragraph', 'This is a local speech performance test. We want to know how quickly the first sound is ready, '
     'and whether the computer can generate the rest faster than it is spoken. '
     'The model stays in memory between requests, so it does not have to load again for every sentence. '
     'For longer documents, streaming lets the player begin while the remaining audio is still being generated. '
     'This example uses a built-in voice and synthetic text. It does not use personal messages or clone anyone\'s voice.'),
]
results = {'device': device, 'speaker': speaker, 'model_load_seconds': round(load_seconds, 3), 'samples': []}
segmenter = pysbd.Segmenter(language='en', clean=False)
for name, text in samples:
    torch.manual_seed(42)
    if device == 'cuda':
        torch.cuda.synchronize()
        torch.cuda.reset_peak_memory_stats()
    start = time.perf_counter()
    chunks = []
    first = None
    # Use Coqui's already-installed sentence splitter without optional spaCy models.
    for sentence in segmenter.segment(text):
        for chunk in model.inference_stream(sentence, 'en', gpt_latent, embedding,
                                            enable_text_splitting=False, stream_chunk_size=20):
            chunk = chunk.detach().cpu().flatten()
            if chunk.numel() == 0:
                continue
            if first is None:
                first = time.perf_counter() - start
                print(f'{name}: first audio after {first:.3f}s', flush=True)
            chunks.append(chunk)
    if not chunks:
        raise RuntimeError('The model returned no audio.')
    elapsed = time.perf_counter() - start
    audio = torch.cat(chunks).numpy()
    duration = len(audio) / 24000
    sf.write(str(output / f'{name}.wav'), audio, 24000, subtype='PCM_16')
    result = {'name': name, 'characters': len(text), 'first_audio_seconds': round(first, 3),
              'generation_seconds': round(elapsed, 3), 'audio_seconds': round(duration, 3),
              'real_time_factor': round(elapsed / duration, 3), 'chunks': len(chunks)}
    if device == 'cuda':
        result['peak_gpu_memory_mb'] = round(torch.cuda.max_memory_allocated() / 1024 ** 2)
    results['samples'].append(result)
    (output / 'metrics.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(json.dumps(result), flush=True)
print(f'Outputs: {output}', flush=True)
