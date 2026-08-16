#!/bin/bash
# ECHO Voice Chat - Interactive voice conversation

echo "═══════════════════════════════════════════════"
echo "  ECHO: Voice Conversation Mode"
echo "  (Supports English & Telugu)"
echo "═══════════════════════════════════════════════"
echo ""

# Show ECHO ready
bash .echo/animate.sh idle

# Initial greeting
powershell -ExecutionPolicy Bypass -File .echo/speak-simple.ps1 "I'm listening. Speak now."

while true; do
    echo ""
    echo "🎤 Press ENTER to speak (10 seconds), or type 'quit' to exit"
    read input

    if [ "$input" = "quit" ]; then
        powershell -ExecutionPolicy Bypass -File .echo/speak-simple.ps1 "Conversation ended."
        break
    fi

    # Listen
    echo "🎤 Recording..."
    RESPONSE=$(python .echo/listen.py)

    if [ -z "$RESPONSE" ]; then
        echo "❌ No speech detected. Try again."
        continue
    fi

    # Parse language and text
    LANG=$(echo "$RESPONSE" | cut -d'|' -f1)
    TEXT=$(echo "$RESPONSE" | cut -d'|' -f2-)

    # Show what was heard
    bash .echo/animate.sh thinking
    echo ""
    echo "👤 YOU ($LANG): $TEXT"
    echo ""

    # Update state based on content
    if echo "$TEXT" | grep -qi "pivot\|change\|different"; then
        TRIGGER="pivot_talk"
        ANIM="edge"
    elif echo "$TEXT" | grep -qi "good\|great\|yes\|launch"; then
        TRIGGER="good_idea"
        ANIM="excited"
    elif echo "$TEXT" | grep -qi "data\|users\|feedback"; then
        TRIGGER="user_data"
        ANIM="excited"
    else
        TRIGGER="good_idea"
        ANIM="thinking"
    fi

    python .echo/update-state.py "$TRIGGER" > /dev/null
    bash .echo/animate.sh "$ANIM"

    # Save founder's input
    echo "{\"speaker\": \"founder\", \"text\": \"$TEXT\", \"language\": \"$LANG\"}" >> .echo/conversation.jsonl

    echo "💭 ECHO is thinking... (Type your response below)"
    echo ""
    echo "ECHO>"
    read ECHO_RESPONSE

    if [ -z "$ECHO_RESPONSE" ]; then
        ECHO_RESPONSE="I'm processing what you said. Continue."
    fi

    # Log ECHO's response
    echo "{\"speaker\": \"echo\", \"text\": \"$ECHO_RESPONSE\"}" >> .echo/conversation.jsonl

    # ECHO speaks
    echo ""
    echo "◆ ECHO: $ECHO_RESPONSE"
    echo ""
    powershell -ExecutionPolicy Bypass -File .echo/speak-simple.ps1 "$ECHO_RESPONSE"

done

echo ""
echo "✅ Voice chat ended. Conversation saved to .echo/conversation.jsonl"
