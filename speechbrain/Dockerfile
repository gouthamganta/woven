FROM python:3.11-slim

WORKDIR /app

# System deps: libsndfile for audio I/O, ffmpeg for format conversion
RUN apt-get update && apt-get install -y --no-install-recommends \
    libsndfile1 \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .

# Install torch CPU-only first (smaller image, no CUDA needed for inference)
RUN pip install --no-cache-dir \
    torch==2.2.2+cpu \
    torchaudio==2.2.2+cpu \
    --index-url https://download.pytorch.org/whl/cpu

RUN pip install --no-cache-dir -r requirements.txt

# Pre-download ECAPA-TDNN model at build time — bakes it into the image.
# Eliminates cold-start model download on first request (~200 MB).
RUN python -c "\
from speechbrain.inference.classifiers import EncoderClassifier; \
EncoderClassifier.from_hparams( \
    source='speechbrain/spkrec-ecapa-voxceleb', \
    run_opts={'device': 'cpu'}, \
    savedir='/tmp/speechbrain_cache')"

COPY main.py .

EXPOSE 8000

# 2 workers: one handles requests while the other loads/processes audio
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000", "--workers", "2"]
