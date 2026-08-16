"""
Woven SpeechBrain voice embedding service.

POST /embed  — body: raw audio bytes (audio/wav)
              returns: JSON float[] of length 192 (ECAPA-TDNN speaker embedding)
GET  /health — liveness/readiness probe
"""

import io
import logging
from contextlib import asynccontextmanager

import torch
import torchaudio
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from speechbrain.inference.classifiers import EncoderClassifier

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger(__name__)

MODEL_SOURCE = "speechbrain/spkrec-ecapa-voxceleb"
SAVEDIR = "/tmp/speechbrain_cache"
TARGET_SR = 16_000
EMBEDDING_DIM = 192

classifier: EncoderClassifier | None = None


@asynccontextmanager
async def lifespan(app: FastAPI):
    global classifier
    logger.info("Loading ECAPA-TDNN model from %s ...", MODEL_SOURCE)
    classifier = EncoderClassifier.from_hparams(
        source=MODEL_SOURCE,
        run_opts={"device": "cpu"},
        savedir=SAVEDIR,
    )
    logger.info("Model loaded — ready to serve")
    yield
    classifier = None


app = FastAPI(title="Woven SpeechBrain", lifespan=lifespan)


@app.post("/embed")
async def embed(request: Request) -> JSONResponse:
    audio_bytes = await request.body()
    if not audio_bytes:
        raise HTTPException(status_code=400, detail="Empty request body — send raw audio/wav bytes")

    try:
        waveform, sample_rate = torchaudio.load(io.BytesIO(audio_bytes))
    except Exception as exc:
        raise HTTPException(status_code=400, detail=f"Could not decode audio: {exc}") from exc

    # Resample to 16 kHz (ECAPA-TDNN requirement)
    if sample_rate != TARGET_SR:
        resampler = torchaudio.transforms.Resample(orig_freq=sample_rate, new_freq=TARGET_SR)
        waveform = resampler(waveform)

    # Mix down to mono
    if waveform.shape[0] > 1:
        waveform = waveform.mean(dim=0, keepdim=True)

    with torch.no_grad():
        embedding = classifier.encode_batch(waveform)  # (1, 1, 192)

    result: list[float] = embedding.squeeze().tolist()

    if len(result) != EMBEDDING_DIM:
        raise HTTPException(status_code=500, detail=f"Unexpected embedding dim: {len(result)}")

    return JSONResponse(result)


@app.get("/health")
def health() -> dict:
    return {"status": "ok", "model_loaded": classifier is not None}
