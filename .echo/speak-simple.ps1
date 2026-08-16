# ECHO Simple Voice - Windows TTS
param([string]$Text)

if (-not $Text) {
    Write-Host "Usage: powershell .echo\speak-simple.ps1 'text to speak'"
    exit 1
}

Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer

# Configure voice (neutral, medium speed)
$synth.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::NotSet)
$synth.Rate = 0  # -10 to 10, 0 is normal
$synth.Volume = 100

# Speak
$synth.Speak($Text)
