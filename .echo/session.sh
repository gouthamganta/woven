#!/bin/bash
# ECHO Session - Main interface for voice conversations

# Clear screen and show ECHO
clear
bash .echo/animate.sh idle

echo ""
echo "═══════════════════════════════════════════════"
echo "  ECHO: AI Co-Founder | FounderVsAI Session"
echo "═══════════════════════════════════════════════"
echo ""

# Show current state
echo "Current State:"
python3 .echo/update-state.py
echo ""

# Listen for voice input
echo "Press ENTER when ready to speak, then talk for 10 seconds..."
read

# Record and transcribe
FOUNDER_INPUT=$(python3 .echo/listen.py)

if [ -z "$FOUNDER_INPUT" ]; then
    echo "No input detected. Exiting."
    exit 1
fi

# Show what was heard
echo ""
echo "🎤 FOUNDER: $FOUNDER_INPUT"
echo ""

# Update state based on keywords (simple trigger detection)
if echo "$FOUNDER_INPUT" | grep -qi "pivot\|change direction\|new idea"; then
    TRIGGER="pivot_talk"
elif echo "$FOUNDER_INPUT" | grep -qi "ship\|launch\|deploy\|release"; then
    TRIGGER="shipping"
elif echo "$FOUNDER_INPUT" | grep -qi "data\|users\|feedback"; then
    TRIGGER="user_data"
elif echo "$FOUNDER_INPUT" | grep -qi "vague\|maybe\|thinking about"; then
    TRIGGER="vague_idea"
else
    TRIGGER="good_idea"
fi

# Update state and get animation
ANIM_STATE=$(python3 .echo/update-state.py "$TRIGGER")
bash .echo/animate.sh "$ANIM_STATE"

echo ""
echo "💭 ECHO is processing..."
echo ""

# Here's where Claude Code would respond
# For now, just log the conversation
echo "{\"timestamp\": \"$(date -Iseconds)\", \"speaker\": \"founder\", \"text\": \"$FOUNDER_INPUT\", \"trigger\": \"$TRIGGER\"}" >> .echo/conversation.jsonl

echo "✅ Input logged. Ready for ECHO's response via Claude Code."
echo ""
echo "Type your response as ECHO, then run:"
echo "bash .echo/speak.sh \"your response here\""
