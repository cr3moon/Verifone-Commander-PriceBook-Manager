<#
    Verifone Commander Price Book Manager - installer

    Run this AS THE USER who will use the app:
        Right-click -> "Run with PowerShell"
      or:
        powershell -ExecutionPolicy Bypass -File .\Install.ps1

    It does two things:
      1. Trusts the app's signing certificate (one-time, asks for admin).
      2. Installs (or updates) the app for the current user (no admin needed).

    Everything the app needs (.NET 8 + Windows App SDK 2.1 runtimes) is bundled
    in the package - there are no other prerequisites to install.
#>

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$cer  = Join-Path $here 'PriceBookManager-DevCert.cer'
$msix = Get-ChildItem -Path $here -Filter *.msix | Select-Object -First 1

Write-Host ''
Write-Host 'Verifone Commander Price Book Manager - installer' -ForegroundColor Cyan
Write-Host '-------------------------------------------------'

if (-not (Test-Path $cer))  { throw "Certificate not found next to this script: $cer" }
if (-not $msix)             { throw "No .msix package found next to this script." }

# --- 1) Trust the signing certificate (LocalMachine\TrustedPeople) -------------
$thumb = ([System.Security.Cryptography.X509Certificates.X509Certificate2]::new($cer)).Thumbprint
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
           Where-Object Thumbprint -eq $thumb

if ($trusted) {
    Write-Host "[1/2] Signing certificate already trusted." -ForegroundColor Green
} else {
    Write-Host "[1/2] Trusting the signing certificate (approve the Administrator prompt)..."
    $cmd = "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile','-Command',$cmd

    $trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
               Where-Object Thumbprint -eq $thumb
    if (-not $trusted) { throw "Certificate was not trusted (admin prompt declined?). Cannot continue." }
    Write-Host "      Certificate trusted." -ForegroundColor Green
}

# --- 2) Install / update the app (current user) --------------------------------
Write-Host "[2/2] Installing $($msix.Name) ..."
Add-AppxPackage -Path $msix.FullName
Write-Host "      Installed." -ForegroundColor Green

Write-Host ''
Write-Host "Done. Launch 'Verifone Commander Price Book Manager' from the Start menu." -ForegroundColor Cyan
Write-Host ''
