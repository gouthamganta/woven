#!/bin/bash
# ECHO Speak - Text to Speech

TEXT="$1"

if [ -z "$TEXT" ]; then
    echo "Usage: bash .echo/speak.sh \"text to speak\""
    exit 1
fi

# Use Piper TTS (will auto-download voice on first run)
echo "$TEXT" | piper --model en_US-lessac-medium --output-file /tmp/echo_voice.wav

# Play the audio (Windows)
if command -v powershell &> /dev/null; then
    powershell -c "(New-Object Media.SoundPlayer '/tmp/echo_voice.wav').PlaySync();"
else
    # Linux fallback
    aplay /tmp/echo_voice.wav 2>/dev/null || ffplay -nodisp -autoexit /tmp/echo_voice.wav 2>/dev/null
fi
