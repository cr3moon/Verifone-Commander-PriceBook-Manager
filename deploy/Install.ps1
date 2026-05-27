<#
    Verifone Commander Price Book Manager - installer

    The package is signed with a publicly-trusted certificate (Futurevest ETP, LLC
    via Azure Artifact Signing), so NO certificate import is required - you can
    even just double-click the .msix. This script is for convenience / scripted
    installs.

    Run:
        Right-click -> "Run with PowerShell"
      or:
        powershell -ExecutionPolicy Bypass -File .\Install.ps1

    Everything the app needs (.NET 8 + Windows App SDK 2.1 runtimes) is bundled
    in the package - there are no other prerequisites to install.
#>

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$msix = Get-ChildItem -Path $here -Filter *.msix | Select-Object -First 1

Write-Host ''
Write-Host 'Verifone Commander Price Book Manager - installer' -ForegroundColor Cyan
Write-Host '-------------------------------------------------'

if (-not $msix) { throw "No .msix package found next to this script." }

Write-Host "Installing $($msix.Name) ..."
Add-AppxPackage -Path $msix.FullName
Write-Host "Installed." -ForegroundColor Green
Write-Host ''
Write-Host "Launch 'Verifone Commander Price Book Manager' from the Start menu." -ForegroundColor Cyan
Write-Host ''
