#!/bin/bash
# ECHO Control - Laptop automation and control

ACTION="$1"
shift
ARGS="$@"

case "$ACTION" in
    open-browser)
        # Open URL in browser
        URL="$ARGS"
        echo "🌐 Opening: $URL"
        start "$URL" || xdg-open "$URL" || open "$URL"
        ;;

    run-backend)
        echo "🚀 Starting Woven backend..."
        cd backend/WovenBackend
        dotnet run &
        cd ../..
        ;;

    run-frontend)
        echo "🎨 Starting Woven frontend..."
        cd frontend/woven-frontend
        npx ng serve --port 4202 &
        cd ../..
        ;;

    database-query)
        # Run SQL query
        QUERY="$ARGS"
        echo "🗄️ Running query: $QUERY"
        psql -h localhost -p 5433 -U postgres -d woven_db -c "$QUERY"
        ;;

    git-status)
        echo "📦 Git status:"
        git status
        ;;

    git-commit)
        MSG="$ARGS"
        echo "💾 Committing: $MSG"
        git add -A
        git commit -m "$MSG"
        ;;

    screenshot)
        echo "📸 Taking screenshot..."
        # Windows: use powershell
        powershell -Command "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{PRTSC}'); Start-Sleep -Milliseconds 500"
        echo "Screenshot saved to clipboard"
        ;;

    system-info)
        echo "💻 System info:"
        echo "OS: $(uname -s)"
        echo "CPU: $(grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d: -f2 | xargs || echo 'Windows')"
        echo "Memory: $(free -h 2>/dev/null | grep Mem | awk '{print $2}' || echo 'Windows')"
        echo "Disk: $(df -h . | tail -1 | awk '{print $4}' || echo 'Windows')"
        ;;

    *)
        echo "Unknown action: $ACTION"
        echo ""
        echo "Available actions:"
        echo "  open-browser <url>"
        echo "  run-backend"
        echo "  run-frontend"
        echo "  database-query \"SELECT ...\""
        echo "  git-status"
        echo "  git-commit \"message\""
        echo "  screenshot"
        echo "  system-info"
        ;;
esac
