<#
.SYNOPSIS
  Installs and compiles the Computer-Use v2 skill for Antigravity CLI (agy).

.DESCRIPTION
  1. Copies SKILL.md and scripts/ into $HOME\.gemini\config\skills\computer-use
  2. Updates SKILL.md paths to match the current user's $HOME directory
  3. Compiles computer.exe & ComputerBridge.dll using the built-in Windows .NET Framework csc.exe (zero external dependencies)
  4. Runs `computer.exe doctor` to verify Per-Monitor-V2 DPI, UIAutomation, and Windows OCR
#>
param(
    [string]$InstallDir = "$HOME\.gemini\config\skills\computer-use"
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "==> Installing computer-use skill to: $InstallDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path "$InstallDir\scripts" | Out-Null

# Copy scripts
Copy-Item "$RepoRoot\scripts\*.cs" "$InstallDir\scripts\" -Force
Copy-Item "$RepoRoot\scripts\computer.ps1" "$InstallDir\scripts\" -Force

# Customize SKILL.md with exact user path for zero-overhead invocation
$skillContent = Get-Content "$RepoRoot\SKILL.md" -Raw
$exeFullPath = Join-Path $InstallDir "scripts\computer.exe"
$skillContent = $skillContent -replace [regex]::Escape('$HOME\.gemini\config\skills\computer-use\scripts\computer.exe'), $exeFullPath
$skillContent = $skillContent -replace 'C:\\Users\\[^\\]+\\\.gemini\\config\\skills\\computer-use\\scripts\\computer\.exe', $exeFullPath
Set-Content -Path "$InstallDir\SKILL.md" -Value $skillContent -Encoding UTF8

Write-Host "==> Compiling native ComputerBridge v2 (computer.exe)..." -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File "$InstallDir\scripts\computer.ps1" --rebuild doctor

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[OK] computer-use skill installed and verified!" -ForegroundColor Green
    Write-Host "Binary location: $exeFullPath" -ForegroundColor Gray
} else {
    Write-Error "Installation verification failed with exit code $LASTEXITCODE"
}
