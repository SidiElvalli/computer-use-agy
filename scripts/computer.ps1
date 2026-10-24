<#
.SYNOPSIS
  Ultra-fast Computer-Use helper for Antigravity CLI (Windows native desktop bridge + UIAutomation + OCR).

.DESCRIPTION
  Usage:
    C:\Users\ahmed\.gemini\config\skills\computer-use\scripts\computer.exe <action> [args...]
    OR
    powershell -NoProfile -ExecutionPolicy Bypass -File computer.ps1 <action> [args...]

  Sources (partial class, compiled together): ComputerBridge.cs Capture.cs Uia.cs Ocr.cs Actions.cs
  Rebuild:  computer.ps1 --rebuild   (auto-rebuilds whenever any .cs file is newer than computer.exe)
#>
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExePath = Join-Path $ScriptDir 'computer.exe'
$DllPath = Join-Path $ScriptDir 'ComputerBridge.dll'
$Sources = Get-ChildItem -Path $ScriptDir -Filter '*.cs' | Select-Object -ExpandProperty FullName

$Csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$WpfDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
$Refs = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
          "/r:$WpfDir\UIAutomationClient.dll", "/r:$WpfDir\UIAutomationTypes.dll", "/r:$WpfDir\WindowsBase.dll")

$needCompile = ($args -contains '--rebuild') -or (-not (Test-Path $ExePath)) -or (-not (Test-Path $DllPath))
if (-not $needCompile) {
    $newest = ($Sources | ForEach-Object { (Get-Item $_).LastWriteTimeUtc } | Measure-Object -Maximum).Maximum
    if ($newest -gt (Get-Item $ExePath).LastWriteTimeUtc) { $needCompile = $true }
}

if ($needCompile) {
    & $Csc /nologo /optimize+ /target:exe /out:"$ExePath" @Refs @Sources
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile computer.exe" }
    & $Csc /nologo /optimize+ /target:library /out:"$DllPath" @Refs @Sources
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile ComputerBridge.dll" }
}

$passArgs = @($args | Where-Object { $_ -ne '--rebuild' })
& $ExePath @passArgs
exit $LASTEXITCODE
