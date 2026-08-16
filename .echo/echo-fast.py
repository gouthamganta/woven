#!/usr/bin/env python3
"""
ECHO - FAST VERSION
Pixel art character, instant response, working mic
"""

from flask import Flask, jsonify
from flask_cors import CORS
import sounddevice as sd
import soundfile as sf
import numpy as np
from faster_whisper import WhisperModel
import pyttsx3
import threading
import queue
import json
import time
from collections import deque

app = Flask(__name__)
CORS(app)

# State
state = {
    "mood": "idle",
    "energy": 70,
    "patience": 60,
    "excitement": 55,
    "mic_level": 0.0,  # Real-time mic level
    "is_listening": False,
    "is_speaking": False,
    "last_speech": ""
}

conversation = deque(maxlen=20)

# Audio settings - MORE SENSITIVE
SAMPLE_RATE = 16000
CHUNK_DURATION = 0.5
CHUNK_SIZE = int(SAMPLE_RATE * CHUNK_DURATION)
SILENCE_THRESHOLD = 0.05  # Based on your mic test (saw 0.1-0.7)
SILENCE_DURATION = 1.0
MIN_SPEECH_DURATION = 0.3
MIC_DEVICE = 1  # Intel Smart Sound microphone

audio_queue = queue.Queue()
speech_buffer = []
silence_chunks = 0

# Fast TTS - Windows native (instant)
tts_engine = pyttsx3.init()
tts_engine.setProperty('rate', 170)
tts_engine.setProperty('volume', 1.0)

# Tiny whisper model for SPEED
whisper_model = WhisperModel("tiny", device="cpu", compute_type="int8")

print("[INIT] Loading complete. Starting...")

def audio_callback(indata, frames, time_info, status):
    """Capture audio + calculate real-time level"""
    audio_queue.put(indata.copy())

    # Calculate mic level for visualization
    volume = np.sqrt(np.mean(indata**2))
    state["mic_level"] = min(1.0, volume * 50)  # Scale for display

def is_speech(audio_chunk):
    """VAD - is this speech?"""
    rms = np.sqrt(np.mean(audio_chunk**2))
    return rms > SILENCE_THRESHOLD

def process_speech():
    """Always listening loop"""
    global speech_buffer, silence_chunks

    print("[MIC] Listening... (speak to see mic level)")

    while True:
        try:
            chunk = audio_queue.get(timeout=1.0)

            if is_speech(chunk):
                speech_buffer.append(chunk)
                silence_chunks = 0

                if len(speech_buffer) == 1:
                    state["is_listening"] = True
                    state["mood"] = "listening"
                    print("[VOICE DETECTED]")
            else:
                if len(speech_buffer) > 0:
                    silence_chunks += 1

                    if silence_chunks >= int(SILENCE_DURATION / CHUNK_DURATION):
                        duration = len(speech_buffer) * CHUNK_DURATION

                        if duration >= MIN_SPEECH_DURATION:
                            audio_data = np.concatenate(speech_buffer)
                            threading.Thread(
                                target=handle_speech,
                                args=(audio_data,),
                                daemon=True
                            ).start()

                        speech_buffer = []
                        silence_chunks = 0
                        state["is_listening"] = False
                        state["mood"] = "idle"

        except queue.Empty:
            continue
        except Exception as e:
            print(f"[ERROR] {e}")

def handle_speech(audio_data):
    """Fast processing"""
    try:
        state["mood"] = "thinking"

        # Transcribe (FAST with tiny model)
        temp_file = ".echo/temp.wav"
        sf.write(temp_file, audio_data, SAMPLE_RATE)

        segments, info = whisper_model.transcribe(temp_file)
        text = " ".join([seg.text for seg in segments]).strip()

        if not text:
            state["mood"] = "idle"
            return

        print(f"\n[YOU]: {text}")

        conversation.append({
            "time": time.time(),
            "speaker": "you",
            "text": text
        })

        # Generate response (INSTANT)
        response = generate_response(text)

        print(f"[ECHO]: {response}\n")

        conversation.append({
            "time": time.time(),
            "speaker": "echo",
            "text": response
        })

        state["last_speech"] = text

        # Speak (FAST - native TTS)
        state["mood"] = "speaking"
        state["is_speaking"] = True

        tts_engine.say(response)
        tts_engine.runAndWait()

        state["mood"] = "idle"
        state["is_speaking"] = False

    except Exception as e:
        print(f"[ERROR] {e}")
        state["mood"] = "idle"
        state["is_speaking"] = False

def generate_response(text):
    """Fast responses"""
    lower = text.lower()

    if "100 women" in lower or "no money" in lower:
        state["patience"] -= 10
        return "Yeah. Possible. You'll hate how. Cold DMs. Show up at cafes. Manual hustle. You willing?"

    elif "pivot" in lower:
        state["patience"] -= 15
        return "Another pivot? Pick one thing. Commit. Execute. Stop chasing shiny objects."

    elif "user" in lower or "traction" in lower:
        state["excitement"] += 10
        return "Numbers. How many users now? How many this week? Show me data."

    elif "ship" in lower or "launch" in lower:
        state["energy"] += 15
        state["excitement"] += 20
        return "YES! What's the first action? Next hour. Let's go."

    else:
        return "I heard you. What's the next action? Not strategy. The thing you'll do now."

# ===== API =====

@app.route('/state')
def get_state():
    return jsonify(state)

@app.route('/conversation')
def get_conversation():
    return jsonify(list(conversation))

@app.route('/status')
def status():
    return jsonify({"status": "running", "model": "tiny", "tts": "native"})

# ===== MAIN =====

if __name__ == "__main__":
    # Start audio
    print("[AUDIO] Starting Intel mic (device 1)...")
    stream = sd.InputStream(
        callback=audio_callback,
        channels=2,  # Intel mic has multiple channels
        samplerate=SAMPLE_RATE,
        blocksize=CHUNK_SIZE,
        device=MIC_DEVICE
    )
    stream.start()

    # Start processor
    thread = threading.Thread(target=process_speech, daemon=True)
    thread.start()

    print("[SERVER] http://localhost:8765")
    print("=" * 60)
    print("READY! JUST TALK!")
    print("=" * 60)

    app.run(host='0.0.0.0', port=8765, debug=False, threaded=True)
