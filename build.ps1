# Builds dist\STT.exe. Stops a running STT.exe from dist first.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'dist\STT.exe'
$stop = Join-Path $HOME '.claude\skills\desktop-app-rules\stop-app.ps1'
if (Test-Path $stop) { & powershell -NoProfile -ExecutionPolicy Bypass -File $stop -Path $exe }
if (-not (Test-Path (Join-Path $root 'assets\stt.ico'))) { & powershell -NoProfile -File (Join-Path $root 'tools\make-icon.ps1') }
dotnet build (Join-Path $root 'STT.csproj') -c Release -o (Join-Path $root 'dist') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-Item $exe | Select-Object Name, Length, LastWriteTime
