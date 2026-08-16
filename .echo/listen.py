#!/usr/bin/env python3
"""
ECHO Listen - Voice Input via faster-whisper
Records audio and transcribes locally
"""

import sounddevice as sd
import soundfile as sf
import numpy as np
from faster_whisper import WhisperModel
import sys
import json
from datetime import datetime

# Config
SAMPLE_RATE = 16000
DURATION = 10  # seconds (adjust as needed)
MODEL_SIZE = "base"  # tiny, base, small, medium, large

def record_audio(duration=DURATION):
    """Record audio from microphone"""
    print(f"🎤 Listening for {duration} seconds...")
    audio = sd.rec(
        int(duration * SAMPLE_RATE),
        samplerate=SAMPLE_RATE,
        channels=1,
        dtype=np.float32
    )
    sd.wait()
    return audio

def transcribe(audio_path):
    """Transcribe audio using faster-whisper (supports English & Telugu)"""
    print("🧠 Transcribing...")
    model = WhisperModel(MODEL_SIZE, device="cpu", compute_type="int8")
    # Auto-detect language (supports English and Telugu)
    segments, info = model.transcribe(audio_path)

    detected_lang = info.language
    print(f"🌐 Detected language: {detected_lang}")

    text = " ".join([segment.text for segment in segments])
    return text.strip(), detected_lang

def main():
    # Record audio
    audio = record_audio()

    # Save temporarily
    temp_file = ".echo/temp_audio.wav"
    sf.write(temp_file, audio, SAMPLE_RATE)

    # Transcribe
    text, lang = transcribe(temp_file)

    if text:
        print(f"\n📝 You said ({lang}): {text}\n")

        # Save to conversation log
        with open(".echo/conversation.jsonl", "a") as f:
            f.write(json.dumps({
                "timestamp": datetime.now().isoformat(),
                "speaker": "founder",
                "text": text,
                "language": lang
            }) + "\n")

        # Output for piping to Claude
        print(f"{lang}|{text}")
    else:
        print("❌ No speech detected")
        sys.exit(1)

if __name__ == "__main__":
    main()
