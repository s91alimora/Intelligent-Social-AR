<#
.SYNOPSIS
    Installs the Piper text-to-speech runtime and the voice models listed in a voice file.

.DESCRIPTION
    Downloads Piper 2023.11.14-2 (piper.exe, eSpeak NG, ONNX Runtime) from its
    official GitHub release, then every voice listed in the voice file, into
    Assets/StreamingAssets/tts/piper/win/. Every download can be verified
    against a SHA-256 checksum.

    Voice file format (JSON array; see Tools/voices.example.json):

      [
        {
          "file":   "narrator.onnx",
          "url":    "https://huggingface.co/rhasspy/piper-voices/resolve/<rev>/en/en_US/.../x.onnx",
          "sha256": "optional checksum of the .onnx file"
        }
      ]

    "file" is the name avatars refer to in their CrossPlatformTTS component
    (Model File Name). The matching configuration file (<url>.json) is
    downloaded next to it unless a local <file>.json already exists, so
    customized configurations are never overwritten.

    Re-running is safe: files that are already present and correct are skipped.

.PARAMETER VoiceFile
    Path to the voice file. Defaults to Tools\voices.json, falling back to
    Tools\voices.example.json.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools\Setup-TTS.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools\Setup-TTS.ps1 -VoiceFile my-voices.json
#>

[CmdletBinding()]
param(
    [string]$VoiceFile
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is very slow with the progress bar on
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$PiperDir    = Join-Path $ProjectRoot "Assets\StreamingAssets\tts\piper\win"
$VoicesDir   = Join-Path $PiperDir "voices"

$PiperRelease = @{
    Url    = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip"
    Sha256 = "f3c58906402b24f3a96d92145f58acba6d86c9b5db896d207f78dc80811efcea"
}

if (-not $VoiceFile) {
    $VoiceFile = Join-Path $PSScriptRoot "voices.json"
    if (-not (Test-Path $VoiceFile)) { $VoiceFile = Join-Path $PSScriptRoot "voices.example.json" }
}

function Test-Checksum([string]$File, [string]$Sha256) {
    if (-not (Test-Path $File)) { return $false }
    if (-not $Sha256) { return $true }
    (Get-FileHash -Algorithm SHA256 $File).Hash -eq $Sha256.ToUpperInvariant()
}

function Get-VerifiedFile([string]$Url, [string]$Destination, [string]$Sha256) {
    if (Test-Checksum $Destination $Sha256) {
        Write-Host "  up to date  $(Split-Path -Leaf $Destination)"
        return
    }
    $partial = "$Destination.part"
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Url -OutFile $partial -UseBasicParsing
            if (Test-Checksum $partial $Sha256) {
                Move-Item -Force $partial $Destination
                Write-Host "  downloaded  $(Split-Path -Leaf $Destination)"
                return
            }
            throw "checksum mismatch"
        }
        catch {
            Write-Warning "  attempt $attempt failed for $(Split-Path -Leaf $Destination): $($_.Exception.Message)"
            Start-Sleep -Seconds ([Math]::Min(3 * $attempt, 15))
        }
    }
    if (Test-Path $partial) { Remove-Item -Force $partial }
    throw "Could not download a verified copy of $Url"
}

New-Item -ItemType Directory -Force -Path $VoicesDir | Out-Null

Write-Host "Piper runtime 2023.11.14-2"
if (Test-Path (Join-Path $PiperDir "piper.exe")) {
    Write-Host "  up to date  piper.exe"
}
else {
    $zip = Join-Path ([IO.Path]::GetTempPath()) "piper_windows_amd64.zip"
    $extract = Join-Path ([IO.Path]::GetTempPath()) "piper_windows_amd64"
    Get-VerifiedFile $PiperRelease.Url $zip $PiperRelease.Sha256
    if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
    Expand-Archive -Path $zip -DestinationPath $extract
    # pkgconfig is build metadata; libtashkeel_model.ort is only used for Arabic voices.
    Get-ChildItem (Join-Path $extract "piper") |
        Where-Object { $_.Name -notin @("pkgconfig", "libtashkeel_model.ort") } |
        Copy-Item -Destination $PiperDir -Recurse -Force
    Remove-Item -Recurse -Force $extract, $zip
    Write-Host "  installed   piper.exe, eSpeak NG, ONNX Runtime"
}

if (-not (Test-Path $VoiceFile)) {
    Write-Warning "No voice file found ($VoiceFile). Only the runtime was installed."
    return
}

$voices = Get-Content -Raw -Path $VoiceFile | ConvertFrom-Json
Write-Host "Voices from $(Split-Path -Leaf $VoiceFile)"
foreach ($voice in $voices) {
    $target = Join-Path $VoicesDir $voice.file
    Get-VerifiedFile $voice.url $target $voice.sha256
    $config = "$target.json"
    if (-not (Test-Path $config)) {
        Get-VerifiedFile "$($voice.url).json" $config $null
    }
}

Write-Host "Done. Text-to-speech is ready."
