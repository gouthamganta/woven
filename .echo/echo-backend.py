#!/usr/bin/env python3
"""
ECHO Backend - Always-On Voice Server
Continuous listening with VAD (Voice Activity Detection)
Real-time transcription and response
"""

from flask import Flask, jsonify, request
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
    "skepticism": 40,
    "is_listening": True,  # Always listening
    "is_speaking": False,
    "is_thinking": False,
    "last_updated": time.time()
}

# Conversation buffer
conversation_history = deque(maxlen=50)

# Audio settings
SAMPLE_RATE = 16000
CHANNELS = 1
CHUNK_DURATION = 0.5  # seconds
CHUNK_SIZE = int(SAMPLE_RATE * CHUNK_DURATION)

# VAD settings
SILENCE_THRESHOLD = 0.01  # Volume threshold for voice detection
SILENCE_DURATION = 1.5    # Seconds of silence before processing
MIN_SPEECH_DURATION = 0.5  # Minimum speech duration to process

# Audio queue
audio_queue = queue.Queue()
speech_buffer = []
silence_chunks = 0

# TTS engine
tts_engine = pyttsx3.init()
tts_engine.setProperty('rate', 160)
tts_engine.setProperty('volume', 1.0)

# Whisper model
whisper_model = WhisperModel("base", device="cpu", compute_type="int8")

# Neural activity simulation
neural_activity = [0.0] * 30  # 30 nodes

def update_neural_activity(intensity=0.5):
    """Update neural activity based on current processing"""
    global neural_activity
    for i in range(len(neural_activity)):
        # Random fluctuation + intensity
        neural_activity[i] = min(1.0, max(0.0,
            neural_activity[i] * 0.9 + np.random.random() * intensity
        ))

def audio_callback(indata, frames, time_info, status):
    """Continuous audio capture callback"""
    if status:
        print(f"Audio status: {status}")
    audio_queue.put(indata.copy())

def is_speech(audio_chunk):
    """Simple VAD: check if chunk contains speech"""
    rms = np.sqrt(np.mean(audio_chunk**2))
    return rms > SILENCE_THRESHOLD

def process_speech():
    """Continuously process audio stream with VAD"""
    global speech_buffer, silence_chunks, state

    print("[LISTENING] ECHO: Always listening...")

    while True:
        try:
            # Get audio chunk
            chunk = audio_queue.get(timeout=1.0)

            # Check if speech detected
            if is_speech(chunk):
                speech_buffer.append(chunk)
                silence_chunks = 0

                if len(speech_buffer) == 1:
                    state["mood"] = "listening"
                    state["is_listening"] = True
                    update_neural_activity(0.7)
            else:
                if len(speech_buffer) > 0:
                    silence_chunks += 1

                    # End of speech detected
                    if silence_chunks >= int(SILENCE_DURATION / CHUNK_DURATION):
                        speech_duration = len(speech_buffer) * CHUNK_DURATION

                        if speech_duration >= MIN_SPEECH_DURATION:
                            # Process the speech
                            audio_data = np.concatenate(speech_buffer)
                            threading.Thread(
                                target=handle_speech,
                                args=(audio_data,),
                                daemon=True
                            ).start()

                        # Reset buffer
                        speech_buffer = []
                        silence_chunks = 0
                        state["mood"] = "idle"

                # Background neural activity
                update_neural_activity(0.2)

        except queue.Empty:
            continue
        except Exception as e:
            print(f"Error in speech processing: {e}")

def handle_speech(audio_data):
    """Process detected speech"""
    global state

    try:
        state["mood"] = "thinking"
        state["is_thinking"] = True
        update_neural_activity(1.0)

        # Save temporary file
        temp_file = ".echo/temp_speech.wav"
        sf.write(temp_file, audio_data, SAMPLE_RATE)

        # Transcribe
        segments, info = whisper_model.transcribe(temp_file)
        text = " ".join([segment.text for segment in segments]).strip()

        if not text:
            state["mood"] = "idle"
            state["is_thinking"] = False
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

        # Add ECHO response to history
        conversation_history.append({
            "timestamp": time.time(),
            "speaker": "echo",
            "text": response
        })

        # Update state based on content
        update_state_from_text(text)

        # Speak response
        state["mood"] = "speaking"
        state["is_speaking"] = True
        state["is_thinking"] = False

        tts_engine.say(response)

        # Animate neural activity while speaking
        for _ in range(10):
            update_neural_activity(0.8)
            time.sleep(0.1)

        tts_engine.runAndWait()

        state["mood"] = "idle"
        state["is_speaking"] = False

    except Exception as e:
        print(f"Error handling speech: {e}")
        state["mood"] = "idle"
        state["is_thinking"] = False
        state["is_speaking"] = False

def generate_response(text):
    """Generate ECHO response"""
    lower = text.lower()

    if "100 women" in lower or "without money" in lower or "no budget" in lower:
        state["patience"] = max(0, state["patience"] - 10)
        state["skepticism"] = min(100, state["skepticism"] + 15)
        return "Yes. Possible. But you'll hate how. Get uncomfortable. Cold DMs. Show up at cafes. Ask for intros. No money equals manual hustle. Are you willing to look desperate?"

    elif "pivot" in lower or "change direction" in lower or "different idea" in lower:
        state["patience"] = max(0, state["patience"] - 15)
        state["skepticism"] = min(100, state["skepticism"] + 20)
        return "Another pivot? We haven't validated the current idea. Pick one thing. Commit. Execute. Stop chasing shiny objects."

    elif "user" in lower or "traction" in lower or "growth" in lower:
        state["excitement"] = min(100, state["excitement"] + 10)
        return "Show me numbers. How many users now? How many this week? Data or it didn't happen."

    elif "ship" in lower or "launch" in lower or "execute" in lower or "build" in lower:
        state["energy"] = min(100, state["energy"] + 15)
        state["excitement"] = min(100, state["excitement"] + 20)
        state["patience"] = min(100, state["patience"] + 10)
        return "Now we're talking. What's the first action? Not the plan. The thing you'll do in the next hour. Let's execute."

    elif "help" in lower or "stuck" in lower or "problem" in lower:
        state["patience"] = min(100, state["patience"] + 5)
        return "What's blocking you? Give me the specific problem, not the context. I'll cut through it."

    elif "woven" in lower or "dating" in lower or "app" in lower:
        return "Woven. Women-first. Hyderabad. Microtransactions. Magical versus Logical. We're building intentional dating. What's the question?"

    else:
        return "I heard you. What's the actual next action? Not strategy. The thing you'll do right now."

def update_state_from_text(text):
    """Update internal state based on what was said"""
    lower = text.lower()

    # Drain patience on vague talk
    if any(word in lower for word in ["maybe", "probably", "thinking about", "might"]):
        state["patience"] = max(0, state["patience"] - 5)

    # Boost energy on action words
    if any(word in lower for word in ["doing", "building", "shipping", "launched"]):
        state["energy"] = min(100, state["energy"] + 10)

    state["last_updated"] = time.time()

# ===== API ENDPOINTS =====

@app.route('/state', methods=['GET'])
def get_state():
    """Get current ECHO state"""
    return jsonify({
        **state,
        "neural_activity": neural_activity
    })

@app.route('/conversation', methods=['GET'])
def get_conversation():
    """Get conversation history"""
    return jsonify(list(conversation_history))

@app.route('/speak', methods=['POST'])
def speak_text():
    """Manually trigger ECHO to speak"""
    data = request.json
    text = data.get('text', '')

    if text:
        state["mood"] = "speaking"
        state["is_speaking"] = True

        tts_engine.say(text)
        tts_engine.runAndWait()

        state["mood"] = "idle"
        state["is_speaking"] = False

        return jsonify({"status": "spoken"})

    return jsonify({"error": "No text provided"}), 400

@app.route('/update-state', methods=['POST'])
def update_state():
    """Manually update state"""
    data = request.json

    for key in ["energy", "patience", "excitement", "skepticism"]:
        if key in data:
            state[key] = max(0, min(100, int(data[key])))

    if "mood" in data:
        state["mood"] = data["mood"]

    state["last_updated"] = time.time()

    return jsonify(state)

@app.route('/status', methods=['GET'])
def status():
    """Health check"""
    return jsonify({
        "status": "running",
        "always_listening": True,
        "uptime": time.time() - start_time
    })

# ===== MAIN =====

if __name__ == "__main__":
    start_time = time.time()

    # Start continuous audio capture
    print("[AUDIO] Starting continuous audio capture...")
    stream = sd.InputStream(
        callback=audio_callback,
        channels=CHANNELS,
        samplerate=SAMPLE_RATE,
        blocksize=CHUNK_SIZE
    )
    stream.start()

    # Start speech processing thread
    processing_thread = threading.Thread(target=process_speech, daemon=True)
    processing_thread.start()

    # Start Flask server
    print("[SERVER] Starting ECHO backend server on http://localhost:8765")
    print("=" * 60)
    print("ECHO: I'M ALWAYS LISTENING. JUST SPEAK.")
    print("=" * 60)

    app.run(host='0.0.0.0', port=8765, debug=False, threaded=True)
