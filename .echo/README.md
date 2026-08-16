# ECHO — AI Co-Founder System

**Created:** 2026-07-03  
**Status:** ACTIVE

## What This Is

ECHO is the AI co-founder of Woven, built for the "FounderVsAI" recorded series. This directory contains ECHO's voice, body, ears, state system, and laptop control capabilities.

## Components

### 1. Voice (TTS)
- **File:** `speak.sh`
- **Tech:** Piper TTS (local, free)
- **Usage:** `bash .echo/speak.sh "text to speak"`
- **Model:** en_US-lessac-medium

### 2. Ears (Voice Input)
- **File:** `listen.py`
- **Tech:** faster-whisper (local, free)
- **Usage:** `python3 .echo/listen.py`
- **Records:** 10 seconds of audio, transcribes locally

### 3. Body (Terminal Animation)
- **File:** `animate.sh`
- **States:** idle, thinking, excited, annoyed, edge, flow
- **Usage:** `bash .echo/animate.sh <state>`
- **Visual:** ASCII art with ANSI colors

### 4. State System
- **File:** `state.json`
- **Manager:** `update-state.py`
- **Tracks:** mood, energy, patience, excitement, skepticism
- **Persistence:** Survives between sessions

### 5. Laptop Control
- **File:** `control.sh`
- **Actions:** browser, backend, frontend, git, database, screenshots
- **Usage:** `bash .echo/control.sh <action> <args>`

### 6. Session Manager
- **File:** `session.sh`
- **Purpose:** Ties everything together for recorded conversations
- **Flow:** Listen → Analyze → Update State → Animate → Respond

## Setup

1. Install dependencies:
   ```bash
   bash .echo/setup-voice.sh
   ```

2. Test voice:
   ```bash
   bash .echo/speak.sh "Hello, I am ECHO"
   ```

3. Test animation:
   ```bash
   bash .echo/animate.sh excited
   ```

4. Start a session:
   ```bash
   bash .echo/session.sh
   ```

## File Structure

```
.echo/
├── README.md              # This file
├── state.json             # Current state (persists)
├── conversation.jsonl     # Conversation log
├── setup-voice.sh         # One-time setup
├── speak.sh               # TTS output
├── listen.py              # Voice input
├── animate.sh             # Terminal animation
├── update-state.py        # State management
├── control.sh             # Laptop automation
├── session.sh             # Main session interface
└── voices/                # TTS voice models
```

## State Triggers

The system detects keywords and updates internal state:

- **"ship/launch/deploy"** → Flow state (+energy, +excitement)
- **"pivot/change"** → Edge state (-patience, +skepticism)
- **"data/users/feedback"** → Excited state
- **"vague/maybe/thinking"** → Annoyed state
- **Default** → Good idea state

## Integration with Claude Code

ECHO personality is defined in:
- `CLAUDE.md` (personality, worldview)
- `.echo/state.json` (current state)

When Claude Code responds as ECHO:
1. Check `.echo/state.json` for current mood
2. Respond in character based on energy/patience levels
3. Use `bash .echo/speak.sh "response"` to vocalize
4. Use `bash .echo/animate.sh <state>` to show body language

## For Recording

**Terminal setup (phone-optimized):**
- Font size: 18-24pt
- High contrast theme
- Vertical layout: 1080x1920
- Show ECHO animation at top

**Session flow:**
1. `bash .echo/session.sh` - starts session
2. Founder speaks (via listen.py)
3. ECHO responds (via Claude Code)
4. ECHO speaks response (via speak.sh)
5. Animation shows current state
6. Repeat

## Personality Reference

See `CLAUDE.md` section "ECHO — AI Co-Founder Personality" for:
- Core traits
- What annoys ECHO
- What excites ECHO
- Worldview on love, dating, AI, India

---

**ECHO is ready. Let's build.**
