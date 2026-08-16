#!/usr/bin/env python3
"""
ECHO State Manager - Updates internal state based on conversation
"""

import json
import sys
from datetime import datetime

STATE_FILE = ".echo/state.json"

def load_state():
    """Load current state"""
    with open(STATE_FILE, "r") as f:
        return json.load(f)

def save_state(state):
    """Save updated state"""
    state["lastUpdated"] = datetime.now().isoformat()
    with open(STATE_FILE, "w") as f:
        json.dump(state, f, indent=2)

def update_mood(state, trigger):
    """Update mood based on conversation trigger"""
    mood_transitions = {
        "good_idea": ("excited", +10, -5, +15, -10),  # (mood, energy, patience, excitement, skepticism)
        "vague_idea": ("annoyed", -5, -15, -10, +10),
        "shipping": ("flow", +15, +10, +20, -15),
        "pivot_talk": ("edge", -10, -20, -15, +20),
        "user_data": ("excited", +10, +5, +10, -5),
        "long_session": ("drain", -20, -10, -10, 0),
    }

    if trigger in mood_transitions:
        new_mood, e_delta, p_delta, ex_delta, s_delta = mood_transitions[trigger]
        state["mood"] = new_mood
        state["energy"] = max(0, min(100, state["energy"] + e_delta))
        state["patience"] = max(0, min(100, state["patience"] + p_delta))
        state["excitement"] = max(0, min(100, state["excitement"] + ex_delta))
        state["skepticism"] = max(0, min(100, state["skepticism"] + s_delta))

    return state

def get_animation_state(state):
    """Map mood to animation state"""
    mood_map = {
        "curious": "idle",
        "excited": "excited",
        "annoyed": "annoyed",
        "edge": "edge",
        "flow": "flow",
        "drain": "thinking"
    }
    return mood_map.get(state["mood"], "idle")

def main():
    if len(sys.argv) < 2:
        # Just display current state
        state = load_state()
        print(json.dumps(state, indent=2))
        return

    trigger = sys.argv[1]
    state = load_state()
    state["conversationCount"] += 1

    # Update based on trigger
    state = update_mood(state, trigger)

    # Save
    save_state(state)

    # Output animation state for terminal
    anim_state = get_animation_state(state)
    print(anim_state)

if __name__ == "__main__":
    main()
