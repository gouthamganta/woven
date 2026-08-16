# ECHO - DEBUG MAP
**What's Working vs What's Broken**

```
┌─────────────────────────────────────────────────────────────────┐
│                     ECHO VOICE SYSTEM                            │
└─────────────────────────────────────────────────────────────────┘

INPUT LAYER (Your Voice → ECHO Ears)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                                                    
   🎤 MICROPHONE                                    
   ├─ Device: Intel Smart Sound (device 1)         ✅ DETECTED
   ├─ Capturing audio: YES                         ✅ WORKING
   ├─ Volume level: 0.1 - 1.7                      ⚠️  TOO QUIET
   └─ Issue: YOU'RE TOO FAR FROM MIC               ❌ BLOCKING
        └─> Need to be LOUD or CLOSE to laptop
   
   ↓
   
   🔊 VOICE ACTIVITY DETECTION (VAD)
   ├─ Threshold: 0.05                              ✅ SET
   ├─ Detecting speech: SOMETIMES                  ⚠️  UNRELIABLE
   └─ Issue: Mic input too low to trigger          ❌ BLOCKING
        └─> Threshold is 0.05, you're at 0.1-0.7
        └─> Need 5x louder OR lower threshold to 0.01
   
   ↓
   
   🧠 WHISPER TRANSCRIPTION
   ├─ Model: "tiny" (fast)                         ✅ INSTALLED
   ├─ Transcribing: NOT TESTED                     ⚠️  UNKNOWN
   └─ Issue: Never receives audio (VAD blocks it)  ❌ BLOCKED


PROCESSING LAYER (ECHO Brain)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

   💭 RESPONSE GENERATION
   ├─ Pattern matching: YES                        ✅ WORKING
   ├─ State updates: YES                           ✅ WORKING
   ├─ Response quality: GOOD                       ✅ WORKING
   └─ No issues here                               ✅ OK


OUTPUT LAYER (ECHO Voice → Your Ears)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

   🔊 TEXT-TO-SPEECH (TTS)
   ├─ Engine: pyttsx3 (Windows native)            ✅ INSTALLED
   ├─ Voice: Microsoft Zira                        ✅ DETECTED
   ├─ Speaking: YES                                ✅ WORKING
   ├─ You hearing it: ???                          ⚠️  YOU SAID NO
   └─ Issue: Either too quiet OR wrong speaker     ❌ PROBLEM
        └─> Check: Windows volume / speaker output
        └─> Test worked (you said "YES I CAN HEAR YOU")
        └─> So TTS DOES work sometimes?


VISUAL LAYER (ECHO Body)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

   🎨 3D PIXEL CHARACTER (Three.js)
   ├─ HTML file: echo-pixel.html                   ✅ CREATED
   ├─ Character model: 3D voxel style              ✅ CREATED
   ├─ Animation: Rotation, bounce, color change    ✅ CODED
   ├─ Loading in browser: ???                      ⚠️  UNKNOWN
   └─ Issues reported: "BUGS IN UI"                ❌ BROKEN
        └─> Possible issues:
            - Three.js not loading (CDN blocked?)
            - CORS errors (file:// protocol)
            - JavaScript errors
            - Backend not connecting (localhost:8765)


INTEGRATION LAYER (Connecting Everything)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

   🌐 FLASK BACKEND (echo-fast.py / echo-real.py)
   ├─ Server: localhost:8765                       ✅ CREATED
   ├─ Running: ???                                 ⚠️  UNKNOWN
   ├─ Endpoints:
   │   ├─ /state                                   ✅ CODED
   │   ├─ /conversation                            ✅ CODED
   │   └─ /status                                  ✅ CODED
   └─ Issue: Never confirmed if server starts      ❌ UNKNOWN
        └─> No logs showing it's running
        └─> Frontend can't connect = UI bugs

   ↓
   
   🔗 FRONTEND ↔ BACKEND CONNECTION
   ├─ HTML → fetch('http://localhost:8765/state')  ✅ CODED
   ├─ CORS enabled: YES                            ✅ CODED
   ├─ Connection working: ???                      ⚠️  UNKNOWN
   └─ Issue: If backend not running, UI breaks     ❌ LIKELY CAUSE
        └─> UI shows "DISCONNECTED"
        └─> Character doesn't animate
        └─> State doesn't update


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
SUMMARY: WHAT'S ACTUALLY BROKEN
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

❌ CRITICAL BLOCKERS:
   1. Microphone input TOO QUIET (0.1-1.7, need 5x louder)
   2. VAD threshold too high (0.05) for your mic level
   3. Backend server may not be running
   4. UI can't connect to backend → "bugs"

⚠️  UNKNOWN STATUS:
   1. Is Flask backend actually running?
   2. Is browser loading the HTML correctly?
   3. Are there JavaScript console errors?
   4. Why did TTS work once but you said "can't hear"?

✅ WORKING:
   1. Microphone EXISTS and captures audio
   2. Response generation (text)
   3. TTS engine (at least tested once)
   4. State management
   5. Code is all written and valid


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
RECOMMENDED FIX ORDER
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

OPTION A: Fix the voice system
   1. Lower VAD threshold from 0.05 → 0.01 (5x more sensitive)
   2. Test mic again - should trigger more easily
   3. Verify Whisper transcription works
   4. Test end-to-end voice flow

OPTION B: Fix the UI first (debug separately)
   1. Start Flask backend manually
   2. Open browser console (F12)
   3. Load echo-pixel.html
   4. Check for errors
   5. Fix connection issues

OPTION C: Skip voice, focus on working text version
   1. Keep using echo-simple-working.py (typing)
   2. Add pixel character to it (no voice input)
   3. You type → ECHO speaks → Character animates
   4. Simplest path to something fully working


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
YOUR CALL
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Which do you want to fix first?
   A = Voice input (mic + VAD)
   B = Visual UI (3D character)
   C = Skip complexity, make typing version perfect

Or:
   D = Fuck all of this, just build Woven features instead
```
