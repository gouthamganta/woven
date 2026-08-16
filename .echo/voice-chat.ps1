# ECHO Voice Chat - Interactive voice conversation (Windows PowerShell)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  ECHO: Voice Conversation Mode" -ForegroundColor Cyan
Write-Host "  (Supports English & Telugu)" -ForegroundColor Cyan
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
$synth.Speak("I'm listening. Speak now.")

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

    if (-not $response) {
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

    # Update state based on content
    if ($text -match "pivot|change|different") {
        $anim = "edge"
        $color = "Red"
    } elseif ($text -match "good|great|yes|launch|ship") {
        $anim = "excited"
        $color = "Green"
    } elseif ($text -match "data|users|feedback") {
        $anim = "excited"
        $color = "Green"
    } else {
        $anim = "thinking"
        $color = "Blue"
    }

    # Save founder's input
    $logEntry = @{
        timestamp = (Get-Date).ToString("o")
        speaker = "founder"
        text = $text
        language = $lang
    } | ConvertTo-Json -Compress
    $logEntry | Out-File -Append -FilePath .echo/conversation.jsonl

    Write-Host "💭 ECHO is thinking... (Type your response below)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "ECHO> " -NoNewline -ForegroundColor Cyan
    $echoResponse = Read-Host

    if (-not $echoResponse) {
        $echoResponse = "I'm processing what you said. Continue."
    }

    # Log ECHO's response
    $echoLog = @{
        timestamp = (Get-Date).ToString("o")
        speaker = "echo"
        text = $echoResponse
    } | ConvertTo-Json -Compress
    $echoLog | Out-File -Append -FilePath .echo/conversation.jsonl

    # Show ECHO response with animation
    Write-Host ""
    Write-Host "    +-------------------+" -ForegroundColor $color
    Write-Host "    |   * E C H O *    |" -ForegroundColor $color
    Write-Host "    +-------------------+" -ForegroundColor $color
    Write-Host "    |     ! ! ! !      |" -ForegroundColor $color
    Write-Host "    |    [  SPARK!  ]   |" -ForegroundColor $color
    Write-Host "    |     ! ! ! !      |" -ForegroundColor $color
    Write-Host "    +-------------------+" -ForegroundColor $color
    Write-Host ""
    Write-Host "◆ ECHO: $echoResponse" -ForegroundColor Cyan
    Write-Host ""

    # ECHO speaks
    $synth.Speak($echoResponse)
}

Write-Host ""
Write-Host "✅ Voice chat ended. Conversation saved to .echo/conversation.jsonl" -ForegroundColor Green
