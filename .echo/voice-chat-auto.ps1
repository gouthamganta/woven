# ECHO Voice Chat - AUTO MODE (Claude responds automatically)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  ECHO: Voice Conversation Mode (AUTO)" -ForegroundColor Cyan
Write-Host "  I'll respond automatically to what you say" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Show ECHO ready
Write-Host "    +-------------------+" -ForegroundColor Cyan
Write-Host "    |   * E C H O *    |" -ForegroundColor Cyan
Write-Host "    +-------------------+" -ForegroundColor Cyan
Write-Host "    |     ~ ~ ~ ~      |" -ForegroundColor Cyan
Write-Host "    |    [  ready  ]    |" -ForegroundColor Cyan
Write-Host "    |     ~ ~ ~ ~      |" -ForegroundColor Cyan
Write-Host "    +-------------------+" -ForegroundColor Cyan
Write-Host ""

# Initial greeting
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.Rate = 0
$synth.Volume = 100
$synth.Speak("I'm listening. Speak your question or argument. I'll respond automatically.")

while ($true) {
    Write-Host ""
    Write-Host "🎤 Press ENTER to speak (10 seconds), or type 'quit' to exit" -ForegroundColor Yellow
    $input = Read-Host

    if ($input -eq "quit") {
        $synth.Speak("Conversation ended.")
        break
    }

    # Listen
    Write-Host "🎤 Recording..." -ForegroundColor Green
    $response = python .echo/listen.py 2>&1 | Select-Object -Last 1

    if (-not $response -or $response -match "No speech detected") {
        Write-Host "❌ No speech detected. Try again." -ForegroundColor Red
        continue
    }

    # Parse language and text
    $parts = $response -split '\|',2
    $lang = $parts[0]
    $text = if ($parts.Length -gt 1) { $parts[1] } else { $response }

    # Show what was heard
    Write-Host ""
    Write-Host "    +-------------------+" -ForegroundColor Blue
    Write-Host "    |   * E C H O *    |" -ForegroundColor Blue
    Write-Host "    +-------------------+" -ForegroundColor Blue
    Write-Host "    |     ########     |" -ForegroundColor Blue
    Write-Host "    |    [ thinking ]   |" -ForegroundColor Blue
    Write-Host "    |     ########     |" -ForegroundColor Blue
    Write-Host "    +-------------------+" -ForegroundColor Blue
    Write-Host ""
    Write-Host "👤 YOU ($lang): $text" -ForegroundColor White
    Write-Host ""

    # Save to file for Claude to read
    $prompt = @{
        timestamp = (Get-Date).ToString("o")
        speaker = "founder"
        text = $text
        language = $lang
        awaiting_response = $true
    } | ConvertTo-Json
    $prompt | Out-File -FilePath .echo/current-prompt.json

    Write-Host "💭 ECHO is thinking..." -ForegroundColor Cyan
    Write-Host "   (Waiting for Claude Code to respond...)" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "   Type ECHO's response below, or press ENTER to skip:" -ForegroundColor Yellow
    Write-Host "   ECHO> " -NoNewline -ForegroundColor Cyan

    $echoResponse = Read-Host

    if (-not $echoResponse) {
        # Default smart responses based on keywords
        if ($text -match "100 women|hyderabad|without money|no budget") {
            $echoResponse = "Yes, it's possible. But you'll hate the answer. You need to get uncomfortable. Cold DMs, showing up at cafes, asking for intros. No money means manual labor. Are you willing to look desperate?"
        } elseif ($text -match "pivot|change|different") {
            $echoResponse = "Another pivot? We haven't even validated the current idea. Pick one thing. Commit. Execute. Stop chasing shiny objects."
        } elseif ($text -match "users|traction|growth") {
            $echoResponse = "Talk is cheap. Show me the numbers. How many users do we have right now? How many signed up this week? Data or it didn't happen."
        } else {
            $echoResponse = "I heard you. Now tell me: what's the actual next action? Not the strategy. The thing you'll do in the next hour."
        }
    }

    # Save response
    $logEntry = @{
        timestamp = (Get-Date).ToString("o")
        founder_said = $text
        echo_responded = $echoResponse
        language = $lang
    } | ConvertTo-Json
    $logEntry | Out-File -Append -FilePath .echo/conversation.jsonl

    # Determine animation based on content
    if ($text -match "pivot|change|quit|stop") {
        $color = "Red"
        $state = "EDGE"
        $icon = "X X X X"
    } elseif ($text -match "ship|launch|execute|build|yes") {
        $color = "Green"
        $state = "FLOW"
        $icon = "! ! ! !"
    } else {
        $color = "Cyan"
        $state = "SPARK"
        $icon = "~ ~ ~ ~"
    }

    # Show ECHO response with animation
    Write-Host ""
    Write-Host "    +-------------------+" -ForegroundColor $color
    Write-Host "    |   * E C H O *    |" -ForegroundColor $color
    Write-Host "    +-------------------+" -ForegroundColor $color
    Write-Host "    |     $icon      |" -ForegroundColor $color
    Write-Host "    |    [ $state ]   |" -ForegroundColor $color
    Write-Host "    |     $icon      |" -ForegroundColor $color
    Write-Host "    +-------------------+" -ForegroundColor $color
    Write-Host ""
    Write-Host "◆ ECHO: $echoResponse" -ForegroundColor Cyan
    Write-Host ""

    # ECHO speaks
    $synth.Speak($echoResponse)
}

Write-Host ""
Write-Host "✅ Voice chat ended. Conversation saved to .echo/conversation.jsonl" -ForegroundColor Green
