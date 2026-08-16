#!/bin/bash
# ECHO Voice Setup - Install TTS and STT

echo "🔊 Setting up ECHO's voice and ears..."

# Install Piper TTS (lightweight, local, free)
echo "Installing Piper TTS..."
pip install piper-tts

# Download a voice model (en_US-lessac-medium is good quality)
mkdir -p .echo/voices
cd .echo/voices
wget https://github.com/rhasspy/piper/releases/download/v1.2.0/voice-en-us-lessac-medium.tar.gz
tar -xzf voice-en-us-lessac-medium.tar.gz
rm voice-en-us-lessac-medium.tar.gz
cd ../..

# Install faster-whisper (local STT)
echo "Installing faster-whisper..."
pip install faster-whisper

# Install sounddevice for audio input
pip install sounddevice soundfile

echo "✅ Voice setup complete!"
echo "Test with: bash .echo/speak.sh \"Hello, I am ECHO\""
