param([switch]$CpuOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$pythonPath = Join-Path $repoRoot 'artifacts\coqui-venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $pythonPath)) {
    py -3.10 -m venv (Join-Path $repoRoot 'artifacts\coqui-venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python 3.10 is required for this setup script.' }
}
& $pythonPath -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'pip setup failed.' }
$wheelIndex = if ($CpuOnly) { 'https://download.pytorch.org/whl/cpu' } else { 'https://download.pytorch.org/whl/cu128' }
& $pythonPath -m pip install torch==2.8.0 torchaudio==2.8.0 --index-url $wheelIndex
if ($LASTEXITCODE -ne 0) { throw 'PyTorch installation failed.' }
& $pythonPath -m pip install kokoro==0.9.4 'misaki[en]==0.9.4' 'transformers>=4.57,<5' soundfile
if ($LASTEXITCODE -ne 0) { throw 'Kokoro installation failed.' }
& $pythonPath -m spacy download en_core_web_sm
if ($LASTEXITCODE -ne 0) { throw 'English language model installation failed.' }
& $pythonPath (Join-Path $PSScriptRoot 'download-kokoro.py')
if ($LASTEXITCODE -ne 0) { throw 'Kokoro model download failed.' }
Write-Output 'Kokoro is ready. Restart the API; Heart US is the default voice.'
