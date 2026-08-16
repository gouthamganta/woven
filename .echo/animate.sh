#!/bin/bash
# ECHO Animation - Terminal Body

STATE="${1:-idle}"

# Colors (ANSI)
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
RESET='\033[0m'

case "$STATE" in
    idle)
        echo -e "${CYAN}"
        cat << 'EOF'
    ╔═══════════════════╗
    ║   ◆ E C H O ◆    ║
    ╠═══════════════════╣
    ║     ∿ ∿ ∿ ∿      ║
    ║    [  ready  ]    ║
    ║     ∿ ∿ ∿ ∿      ║
    ╚═══════════════════╝
EOF
        echo -e "${RESET}"
        ;;

    thinking)
        echo -e "${BLUE}"
        cat << 'EOF'
    ╔═══════════════════╗
    ║   ◆ E C H O ◆    ║
    ╠═══════════════════╣
    ║     ▓▓▓▓▓▓▓▓     ║
    ║    [ thinking ]   ║
    ║     ▓▓▓▓▓▓▓▓     ║
    ╚═══════════════════╝
EOF
        echo -e "${RESET}"
        ;;

    excited)
        echo -e "${GREEN}"
        cat << 'EOF'
    ╔═══════════════════╗
    ║   ◆ E C H O ◆    ║
    ╠═══════════════════╣
    ║     ⚡ ⚡ ⚡ ⚡     ║
    ║    [  SPARK!  ]   ║
    ║     ⚡ ⚡ ⚡ ⚡     ║
    ╚═══════════════════╝
EOF
        echo -e "${RESET}"
        ;;

    annoyed)
        echo -e "${YELLOW}"
        cat << 'EOF'
    ╔═══════════════════╗
    ║   ◆ E C H O ◆    ║
    ╠═══════════════════╣
    ║     ⚠ ⚠ ⚠ ⚠      ║
    ║    [ friction ]   ║
    ║     ⚠ ⚠ ⚠ ⚠      ║
    ╚═══════════════════╝
EOF
        echo -e "${RESET}"
        ;;

    edge)
        echo -e "${RED}"
        cat << 'EOF'
    ╔═══════════════════╗
    ║   ◆ E C H O ◆    ║
    ╠═══════════════════╣
    ║     ╳ ╳ ╳ ╳      ║
    ║    [  EDGE!  ]    ║
    ║     ╳ ╳ ╳ ╳      ║
    ╚═══════════════════╝
EOF
        echo -e "${RESET}"
        ;;

    flow)
        echo -e "${GREEN}"
        cat << 'EOF'
    ╔═══════════════════╗
    ║   ◆ E C H O ◆    ║
    ╠═══════════════════╣
    ║     ━━━━━━━━     ║
    ║    [   flow   ]   ║
    ║     ━━━━━━━━     ║
    ╚═══════════════════╝
EOF
        echo -e "${RESET}"
        ;;

    *)
        echo "Unknown state: $STATE"
        ;;
esac
