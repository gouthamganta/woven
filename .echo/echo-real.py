#!/usr/bin/env python3
"""
ECHO - Real Human Interface
Natural voice with Edge TTS
Continuous listening with better VAD
"""

from flask import Flask, jsonify, request, send_file
from flask_cors import CORS
import sounddevice as sd
import soundfile as sf
import numpy as np
from faster_whisper import WhisperModel
import asyncio
import edge_tts
import threading
import queue
import json
import time
import os
from collections import deque

app = Flask(__name__)
CORS(app)

# State
state = {
    "mood": "idle",
    "energy": 70,
    "patience": 60,
    "excitement": 55,
    "skepticism": 40,
    "is_listening": True,
    "is_speaking": False,
    "is_thinking": False,
    "last_updated": time.time(),
    "mouth_position": 0.0,  # 0.0 = closed, 1.0 = open
    "expression": "neutral"  # neutral, happy, annoyed, excited
}

# Conversation
conversation_history = deque(maxlen=50)

# Audio settings
SAMPLE_RATE = 16000
CHUNK_DURATION = 0.3
CHUNK_SIZE = int(SAMPLE_RATE * CHUNK_DURATION)
SILENCE_THRESHOLD = 0.015
SILENCE_DURATION = 1.2
MIN_SPEECH_DURATION = 0.4

# Queues
audio_queue = queue.Queue()
speech_buffer = []
silence_chunks = 0

# Whisper
whisper_model = WhisperModel("base", device="cpu", compute_type="int8")

# Voice settings - realistic female voice
VOICE = "en-US-AriaNeural"  # Professional, warm female voice

def audio_callback(indata, frames, time_info, status):
    """Continuous audio capture"""
    if status:
        print(f"[AUDIO] {status}")
    audio_queue.put(indata.copy())

def is_speech(audio_chunk):
    """Improved VAD"""
    rms = np.sqrt(np.mean(audio_chunk**2))
    return rms > SILENCE_THRESHOLD

async def speak_with_edge_tts(text):
    """Speak using Edge TTS - natural human voice"""
    try:
        state["is_speaking"] = True
        state["mood"] = "speaking"
        state["expression"] = "talking"

        # Generate speech
        output_file = ".echo/temp_speech.mp3"
        communicate = edge_tts.Communicate(text, VOICE, rate="+5%")
        await communicate.save(output_file)

        # Play the audio
        data, samplerate = sf.read(output_file)

        # Animate mouth while playing
        duration = len(data) / samplerate
        start_time = time.time()

        def update_mouth():
            while time.time() - start_time < duration:
                # Oscillate mouth position while speaking
                t = (time.time() - start_time) * 10
                state["mouth_position"] = 0.3 + 0.5 * abs(np.sin(t))
                time.sleep(0.05)
            state["mouth_position"] = 0.0

        mouth_thread = threading.Thread(target=update_mouth, daemon=True)
        mouth_thread.start()

        # Play audio
        sd.play(data, samplerate)
        sd.wait()

        # Clean up
        if os.path.exists(output_file):
            os.remove(output_file)

        state["is_speaking"] = False
        state["mood"] = "idle"
        state["expression"] = "neutral"

    except Exception as e:
        print(f"[TTS ERROR] {e}")
        state["is_speaking"] = False
        state["mood"] = "idle"

def process_speech():
    """Continuously process audio with VAD"""
    global speech_buffer, silence_chunks, state

    print("[LISTENING] ECHO: Always listening for your voice...")

    while True:
        try:
            chunk = audio_queue.get(timeout=1.0)

            if is_speech(chunk):
                speech_buffer.append(chunk)
                silence_chunks = 0

                if len(speech_buffer) == 1:
                    state["mood"] = "listening"
                    state["expression"] = "focused"
                    print("[VOICE DETECTED] Listening...")
            else:
                if len(speech_buffer) > 0:
                    silence_chunks += 1

                    if silence_chunks >= int(SILENCE_DURATION / CHUNK_DURATION):
                        speech_duration = len(speech_buffer) * CHUNK_DURATION

                        if speech_duration >= MIN_SPEECH_DURATION:
                            audio_data = np.concatenate(speech_buffer)
                            threading.Thread(
                                target=handle_speech,
                                args=(audio_data,),
                                daemon=True
                            ).start()

                        speech_buffer = []
                        silence_chunks = 0
                        state["mood"] = "idle"
                        state["expression"] = "neutral"

        except queue.Empty:
            continue
        except Exception as e:
            print(f"[ERROR] Speech processing: {e}")

def handle_speech(audio_data):
    """Process detected speech"""
    global state

    try:
        state["mood"] = "thinking"
        state["expression"] = "thinking"

        # Save and transcribe
        temp_file = ".echo/temp_input.wav"
        sf.write(temp_file, audio_data, SAMPLE_RATE)

        segments, info = whisper_model.transcribe(temp_file)
        text = " ".join([segment.text for segment in segments]).strip()

        if not text:
            state["mood"] = "idle"
            state["expression"] = "neutral"
            return

        detected_lang = info.language

        print(f"\n[YOU] ({detected_lang}): {text}")

        # Add to conversation
        conversation_history.append({
            "timestamp": time.time(),
            "speaker": "founder",
            "text": text,
            "language": detected_lang
        })

        # Generate response
        response = generate_response(text)

        print(f"[ECHO]: {response}\n")

        # Add to conversation
        conversation_history.append({
            "timestamp": time.time(),
            "speaker": "echo",
            "text": response
        })

        # Update state
        update_state_from_text(text)

        # Speak response using Edge TTS
        asyncio.run(speak_with_edge_tts(response))

    except Exception as e:
        print(f"[ERROR] Handle speech: {e}")
        state["mood"] = "idle"
        state["expression"] = "neutral"

def generate_response(text):
    """Generate ECHO response"""
    lower = text.lower()

    if "100 women" in lower or "without money" in lower or "no budget" in lower:
        state["patience"] = max(0, state["patience"] - 10)
        state["skepticism"] = min(100, state["skepticism"] + 15)
        state["expression"] = "skeptical"
        return "Yes. It's possible. But you'll hate how. You need to get uncomfortable. Cold DMs. Show up at cafes. Ask for intros. No money means manual hustle. Are you willing to look desperate?"

    elif "pivot" in lower or "change direction" in lower:
        state["patience"] = max(0, state["patience"] - 15)
        state["skepticism"] = min(100, state["skepticism"] + 20)
        state["expression"] = "annoyed"
        return "Another pivot? We haven't validated the current idea. Pick one thing. Commit. Execute. Stop chasing shiny objects."

    elif "user" in lower or "traction" in lower or "growth" in lower:
        state["excitement"] = min(100, state["excitement"] + 10)
        state["expression"] = "interested"
        return "Show me the numbers. How many users now? How many this week? Data or it didn't happen."

    elif "ship" in lower or "launch" in lower or "execute" in lower or "build" in lower:
        state["energy"] = min(100, state["energy"] + 15)
        state["excitement"] = min(100, state["excitement"] + 20)
        state["expression"] = "excited"
        return "Now we're talking! What's the first action? Not the plan. The thing you'll do in the next hour. Let's execute."

    elif "help" in lower or "stuck" in lower:
        state["patience"] = min(100, state["patience"] + 5)
        state["expression"] = "focused"
        return "What's blocking you? Give me the specific problem. I'll cut through it."

    else:
        state["expression"] = "neutral"
        return "I heard you. What's the actual next action? Not strategy. The thing you'll do right now."

def update_state_from_text(text):
    """Update internal state"""
    lower = text.lower()

    if any(word in lower for word in ["maybe", "probably", "thinking about", "might"]):
        state["patience"] = max(0, state["patience"] - 5)

    if any(word in lower for word in ["doing", "building", "shipping", "launched"]):
        state["energy"] = min(100, state["energy"] + 10)

    state["last_updated"] = time.time()

# ===== API ENDPOINTS =====

@app.route('/state', methods=['GET'])
def get_state():
    return jsonify(state)

@app.route('/conversation', methods=['GET'])
def get_conversation():
    return jsonify(list(conversation_history))

@app.route('/status', methods=['GET'])
def status():
    return jsonify({
        "status": "running",
        "always_listening": True,
        "voice": VOICE,
        "uptime": time.time() - start_time
    })

# ===== MAIN =====

if __name__ == "__main__":
    start_time = time.time()

    # Create temp directory
    os.makedirs(".echo", exist_ok=True)

    # Start audio capture
    print("[AUDIO] Starting continuous microphone capture...")
    stream = sd.InputStream(
        callback=audio_callback,
        channels=1,
        samplerate=SAMPLE_RATE,
        blocksize=CHUNK_SIZE
    )
    stream.start()

    # Start speech processing
    processing_thread = threading.Thread(target=process_speech, daemon=True)
    processing_thread.start()

    # Start server
    print("[SERVER] ECHO backend on http://localhost:8765")
    print("=" * 60)
    print("I'M ALWAYS LISTENING. JUST TALK TO ME.")
    print(f"Voice: {VOICE}")
    print("=" * 60)

    app.run(host='0.0.0.0', port=8765, debug=False, threaded=True)
