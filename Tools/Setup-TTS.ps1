<#
.SYNOPSIS
    Downloads the Piper text-to-speech runtime and the study's voice models.

.DESCRIPTION
    The TTS engine and voice models are not stored in this repository: several
    voices come from datasets whose licenses do not allow redistribution here.
    This script fetches them from their official distributors instead:

      * Piper runtime 2023.11.14-2 (piper.exe, eSpeak NG, ONNX Runtime)
        from https://github.com/rhasspy/piper/releases
      * Voice models (.onnx) from https://huggingface.co/rhasspy/piper-voices,
        pinned to a fixed revision

    Every download is checked against a SHA-256 checksum, so the files are
    byte-identical to the ones used in the study. The small voice configuration
    files (*.onnx.json) are part of the repository and are not downloaded.

    Run it once after cloning. Re-running is safe: files that are already
    present and correct are skipped.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools\Setup-TTS.ps1
#>

[CmdletBinding()]
param()

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

$VoicesRevision = "c10ece1aade47bb51c153c893d14e5bf8e5b7117"
$VoicesBaseUrl  = "https://huggingface.co/rhasspy/piper-voices/resolve/$VoicesRevision"

# Local file name (as referenced by the avatar prefabs) -> official file.
$Voices = @(
    @{ File = "alan.onnx";          Path = "en/en_GB/northern_english_male/medium/en_GB-northern_english_male-medium.onnx"; Sha256 = "57a219ae8e638873db7d18893304be5069c42868f392bb95c3ff17f0690d0689" }
    @{ File = "alba.onnx";          Path = "en/en_GB/alba/medium/en_GB-alba-medium.onnx";                                   Sha256 = "401369c4a81d09fdd86c32c5c864440811dbdcc66466cde2d64f7133a66ad03b" }
    @{ File = "arctic_man.onnx";    Path = "en/en_US/arctic/medium/en_US-arctic-medium.onnx";                               Sha256 = "483303e294947a3ec2f910ea96093d876e1640f5772e9d89e511d6c82c667286" }
    @{ File = "bryce.onnx";         Path = "en/en_US/bryce/medium/en_US-bryce-medium.onnx";                                 Sha256 = "dc9caa6c313199ffb5ac698b6e542fa6cba388aeaf2731e25262e33b9810aef1" }
    @{ File = "cori.onnx";          Path = "en/en_GB/cori/high/en_GB-cori-high.onnx";                                       Sha256 = "470b4dd634c98f8a4850d7626ffc3dfc90774628eeef6605a6dd8f88f30a5903" }
    @{ File = "jenny.onnx";         Path = "en/en_GB/jenny_dioco/medium/en_GB-jenny_dioco-medium.onnx";                     Sha256 = "469c630d209e139dd392a66bf4abde4ab86390a0269c1e47b4e5d7ce81526b01" }
    @{ File = "joe.onnx";           Path = "en/en_US/joe/medium/en_US-joe-medium.onnx";                                     Sha256 = "58afce0321b8d9c46d7cdf9c16500cc55a793b4220212dba6b70fb788b3baf06" }
    @{ File = "john.onnx";          Path = "en/en_US/john/medium/en_US-john-medium.onnx";                                   Sha256 = "789c6c875726e627ddee93d51d8727859abe9c091c3d141591f4b83c2072e988" }
    @{ File = "kathleen.onnx";      Path = "en/en_US/kathleen/low/en_US-kathleen-low.onnx";                                 Sha256 = "87adf17f5326bc0782282147a8b9788406236245f0f9b0e68dacb651bc1de8b6" }
    @{ File = "kristin.onnx";       Path = "en/en_US/kristin/medium/en_US-kristin-medium.onnx";                             Sha256 = "5849957f929cbf720c258f8458692d6103fff2f0e3d3b19c8259474bb06a18d4" }
    @{ File = "layla.onnx";         Path = "en/en_US/ljspeech/high/en_US-ljspeech-high.onnx";                               Sha256 = "5d4f08ba6a2a48c44592eed3ce56bf85e9de3dd4e20df90541ae68a8310c029a" }
    @{ File = "lessac_female.onnx"; Path = "en/en_US/lessac/medium/en_US-lessac-medium.onnx";                               Sha256 = "5efe09e69902187827af646e1a6e9d269dee769f9877d17b16b1b46eeaaf019f" }
    @{ File = "norman.onnx";        Path = "en/en_US/norman/medium/en_US-norman-medium.onnx";                               Sha256 = "b9739443232a80a59c7d18810dd856899bf16a7964725f5ab81ea49b1351cb71" }
    @{ File = "ryan.onnx";          Path = "en/en_US/ryan/high/en_US-ryan-high.onnx";                                       Sha256 = "b3990d7606e183ec8dbfba70a4607074f162de1a0c412e0180d1ff60bb154eca" }
)

# Voices used by the study that have no verified official source yet (see README).
$UnresolvedVoices = @("amy.onnx", "tony.onnx")

function Test-Checksum([string]$File, [string]$Sha256) {
    (Test-Path $File) -and ((Get-FileHash -Algorithm SHA256 $File).Hash -eq $Sha256.ToUpperInvariant())
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

Write-Host "Voice models (rhasspy/piper-voices @ $($VoicesRevision.Substring(0, 7)))"
foreach ($voice in $Voices) {
    Get-VerifiedFile "$VoicesBaseUrl/$($voice.Path)" (Join-Path $VoicesDir $voice.File) $voice.Sha256
}

$missing = $UnresolvedVoices | Where-Object { -not (Test-Path (Join-Path $VoicesDir $_)) }
if ($missing) {
    Write-Warning ("Not available from an official source yet: " + ($missing -join ", ") +
        ". The avatars using these voices will be silent. See README > Known limitations.")
}

Write-Host "Done. Text-to-speech is ready."
