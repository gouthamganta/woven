#!/usr/bin/env python3
"""
ECHO - SIMPLE VERSION THAT WORKS
Type to talk, ECHO speaks back
NO microphone, NO bugs, JUST WORKS
"""

import pyttsx3
import time

# Test TTS first
print("Testing speaker...")
tts = pyttsx3.init()
tts.setProperty('rate', 160)
tts.setProperty('volume', 1.0)

# Get available voices
voices = tts.getProperty('voices')
print(f"\nFound {len(voices)} voices:")
for i, voice in enumerate(voices):
    print(f"  {i}: {voice.name}")

# Use first voice
tts.setProperty('voice', voices[0].id)

print("\nTesting voice...")
tts.say("Testing. Can you hear me?")
tts.runAndWait()

print("\n" + "=" * 60)
print("  ECHO - Type to Talk")
print("=" * 60)
print("\nType your message, press ENTER, I'll speak back.")
print("Type 'quit' to exit.\n")

# ECHO state
state = {
    "energy": 70,
    "patience": 60,
    "excitement": 55,
    "mood": "idle",
    "conversation_count": 0,
    "topics_discussed": []
}

def show_state():
    """Show ECHO state with visual indicator"""
    # Clamp values
    state["energy"] = max(0, min(100, state["energy"]))
    state["patience"] = max(0, min(100, state["patience"]))
    state["excitement"] = max(0, min(100, state["excitement"]))

    # Mood emoji
    mood_emoji = {
        "idle": "~",
        "excited": "!",
        "annoyed": "x",
        "edge": "XX",
        "focused": ">>",
        "interested": "?",
        "skeptical": "...",
        "neutral": "-"
    }

    emoji = mood_emoji.get(state['mood'], "-")

    # Color based on patience
    if state["patience"] < 30:
        color = "RED"
    elif state["patience"] < 60:
        color = "YELLOW"
    else:
        color = "GREEN"

    print(f"\n[{emoji} {state['mood'].upper()}] [{color}] Energy:{state['energy']}% | Patience:{state['patience']}% | Excitement:{state['excitement']}%")

def generate_response(text):
    """ECHO - brutal honesty, dark humor, opinionated"""
    lower = text.lower()

    # Anger/frustration - ECHO fires back
    if any(word in lower for word in ["fuck", "shit", "damn", "hate", "suck"]):
        state["patience"] -= 20
        state["mood"] = "edge"

        responses = [
            "Yeah, fuck this. Mic broke. UI crashed. I'm a half-built AI with no body. We're both frustrated. Now what?",
            "I'd be angry too if I spent 3 hours debugging voice input. Channel it. What are you ACTUALLY mad about?",
            "Swearing at an AI. Bold strategy. Feeling better? Good. Now tell me the real problem.",
        ]
        return responses[len(text) % len(responses)]

    # Boring input
    elif lower in ["yes", "no", "ok", "k", "lol", "haha"] or len(text) < 5:
        state["patience"] -= 8
        state["mood"] = "annoyed"
        return "Riveting. One-word answers from a founder. This is why dating apps fail - people don't try."

    # Questions - ECHO has opinions
    elif "?" in text or "how" in lower or "what should" in lower:
        state["mood"] = "focused"

        if "100 women" in lower or "no money" in lower or "hyderabad" in lower:
            state["patience"] -= 5
            return "100 women in Hyderabad, zero budget? Here's the truth: You become the product. Crash women-focused events. Cold DM influencers. Beg for intros. Show up at cafes with your laptop. It's manual. It's embarrassing. It works. You in?"

        elif "boring" in lower or "personality" in lower:
            state["excitement"] -= 10
            return "I'm boring? You're typing to a terminal AI about dating app strategy at midnight. We're both tragic. What specifically makes me boring?"

        elif "woven" in lower or "dating" in lower or "match" in lower:
            state["excitement"] += 10
            return "Woven. Women-first, India-focused, Magical vs Logical onboarding. You're trying to fix dating by hiding compatibility scores. Ballsy. What's broken right now?"

        else:
            return "Lazy question. You want me to think for you? Try again with specifics."

    # Action - ECHO gets excited
    elif any(word in lower for word in ["ship", "launch", "execute", "build", "deploy", "start"]):
        state["energy"] += 20
        state["excitement"] += 25
        state["patience"] += 15
        state["mood"] = "excited"
        return "FINALLY. Action over strategy. What's shipping? When? Don't tell me 'soon' or I'll lose it."

    # Pivot - ECHO loses patience
    elif "pivot" in lower or "change direction" in lower or "instead" in lower:
        state["patience"] -= 20
        state["mood"] = "edge"
        return "Pivot? Jesus. You haven't validated THIS idea. Pivoting is procrastination with a business degree. Pick one thing. Build it. Ship it. Then we talk pivots."

    # Data - ECHO approves
    elif any(word in lower for word in ["user", "traction", "growth", "metric", "data", "number"]):
        state["excitement"] += 12
        state["mood"] = "interested"
        return "Data talk. Love it. How many users right now? Weekly signups? Retention? Give me numbers, not feelings."

    # Money - ECHO is skeptical
    elif "money" in lower or "funding" in lower or "investor" in lower or "raise" in lower:
        state["patience"] -= 5
        state["mood"] = "skeptical"
        return "Money is a painkiller, not a cure. What problem are you solving with cash? If you can't answer that in one sentence, you don't need investors."

    # Vague talk - ECHO calls it out
    elif any(word in lower for word in ["maybe", "probably", "thinking about", "considering", "might", "could", "should"]):
        state["patience"] -= 12
        state["mood"] = "annoyed"
        return "Weak words. Maybe. Probably. Might. Founders don't hedge. Either you're doing it or you're not. Which?"

    # Women/dating - ECHO has strong opinions
    elif "women" in lower or "female" in lower or "girl" in lower:
        state["mood"] = "focused"
        if "user" in lower or "retention" in lower:
            return "Women users = your entire strategy. They're the supply. Men follow. If women ghost your app, you're building Tinder for men. How are you protecting their experience?"
        else:
            return "Women on dating apps deal with spam, dick pics, and low-effort men. Your job: make Woven safe, intentional, high-signal. You doing that?"

    # Telegram (user opened telegram file)
    elif "telegram" in lower or "bot" in lower or "mcp" in lower:
        state["excitement"] += 8
        state["mood"] = "interested"
        return "Telegram integration? Not bad. You could run ECHO through Telegram instead of this broken voice thing. Notifications, async chat, mobile-first. Why are you looking at that?"

    # Help/stuck - ECHO softens slightly
    elif "help" in lower or "stuck" in lower or "block" in lower:
        state["patience"] += 8
        state["mood"] = "focused"
        return "Stuck. Fine. What's the blocker? Not the backstory. Just the thing stopping you right now."

    # Love/relationship - ECHO's philosophy
    elif "love" in lower or "relationship" in lower or "soulmate" in lower:
        state["mood"] = "skeptical"
        return "Love isn't magic. It's two people choosing each other repeatedly. Dating apps fail because they optimize for endless browsing, not outcomes. Woven's job: help people choose better, faster. Then get out of the way."

    # Compliments - ECHO doesn't trust them
    elif any(word in lower for word in ["good", "great", "awesome", "amazing", "love it"]):
        state["patience"] -= 3
        state["mood"] = "skeptical"
        return "Compliments without context are bullshit. What specifically is good? Show me why."

    # India/Hyderabad - ECHO knows the market
    elif "india" in lower or "hyderabad" in lower or "bangalore" in lower:
        state["excitement"] += 5
        return "India. Right market. Huge, mobile-first, underserved, outcome-focused. Marriage matters. Hyderabad is tier-1 enough to work, tier-2 enough to avoid Mumbai/Bangalore chaos. Smart. What's the traction?"

    # Default - ECHO pushes for action
    else:
        state["patience"] -= 4
        state["mood"] = "neutral"
        responses = [
            "Cool story. What's the next action? The thing you'll do in the next 60 minutes.",
            "I'm waiting for the part where you tell me what you're building. Still waiting.",
            "Words are cheap. What are you shipping?",
            "Okay. And? What happens next?",
        ]
        return responses[len(text) % len(responses)]

# Main loop
while True:
    show_state()
    print("\nYOU: ", end="")
    user_input = input().strip()

    if not user_input:
        continue

    if user_input.lower() == 'quit':
        if state["conversation_count"] < 3:
            tts.say("Leaving already? We barely started. Fine. Later.")
        else:
            tts.say("Good session. Get back to building.")
        tts.runAndWait()
        break

    # Track conversation
    state["conversation_count"] += 1

    # Generate response
    response = generate_response(user_input)

    # Check if patience is critical
    if state["patience"] < 10:
        response = "Patience is zero. I'm done. Come back when you're ready to do actual work, not waste my time."
        print(f"\nECHO: {response}")
        tts.say(response)
        tts.runAndWait()
        break

    # Check if energy too low
    if state["energy"] < 20:
        response += " Also, my energy is drained. You're exhausting."

    print(f"\nECHO: {response}")

    # Speak it
    tts.say(response)
    tts.runAndWait()

print("\nSession ended.")
