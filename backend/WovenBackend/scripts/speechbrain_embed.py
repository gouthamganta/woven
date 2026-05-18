#!/usr/bin/env python3
"""
speechbrain_embed.py
Produces a 192-dim ECAPA-TDNN speaker embedding from an audio file.
Usage:
    python3 speechbrain_embed.py <audio_path>
    python3 speechbrain_embed.py --test
Output:
    JSON array of 192 floats to stdout.
"""

import sys
import json
import os

def embed(audio_path: str) -> list:
    import torch
    import torchaudio
    from speechbrain.pretrained import EncoderClassifier

    model = EncoderClassifier.from_hparams(
        source="speechbrain/spkrec-ecapa-voxceleb",
        savedir="/tmp/speechbrain_cache",
        run_opts={"device": "cpu"}
    )

    signal, fs = torchaudio.load(audio_path)

    # Resample to 16kHz if needed
    if fs != 16000:
        resampler = torchaudio.transforms.Resample(orig_freq=fs, new_freq=16000)
        signal = resampler(signal)

    # Use first channel if stereo
    if signal.shape[0] > 1:
        signal = signal.mean(dim=0, keepdim=True)

    with torch.no_grad():
        embedding = model.encode_batch(signal)

    vec = embedding.squeeze().cpu().numpy().tolist()

    # Ensure exactly 192 dims
    if len(vec) > 192:
        vec = vec[:192]
    elif len(vec) < 192:
        vec = vec + [0.0] * (192 - len(vec))

    return vec

def run_test():
    import torch
    import numpy as np
    # Return a zero vector to verify the script loads correctly
    vec = [0.0] * 192
    print(json.dumps(vec))

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: speechbrain_embed.py <audio_path> | --test", file=sys.stderr)
        sys.exit(1)

    if sys.argv[1] == "--test":
        run_test()
        sys.exit(0)

    audio_path = sys.argv[1]
    if not os.path.exists(audio_path):
        print(f"File not found: {audio_path}", file=sys.stderr)
        sys.exit(1)

    try:
        vec = embed(audio_path)
        print(json.dumps(vec))
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)
