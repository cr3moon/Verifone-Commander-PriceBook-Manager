============================================================
 Verifone Commander Price Book Manager - Install Package
============================================================

WHAT THIS IS
  A self-contained Windows app (MSIX). The .NET 8 runtime and the
  Windows App SDK 2.1 runtime are bundled inside the package, so the
  target PC needs NO other downloads or runtime installs.

  The package is digitally signed by Futurevest ETP, LLC (DBA Exact
  Technology Partners) with a publicly-trusted certificate via Azure
  Artifact Signing. No certificate import is required.

REQUIREMENTS
  - Windows 10 version 2004 (build 19041) or newer, 64-bit
  - That's it. No .NET install, no Windows App Runtime install, no cert import.

FILES IN THIS FOLDER
  - VerifoneCommander.PriceBookManager.DesktopApp_<version>_x64.msix
        The signed application package (~83 MB; ~209 MB once installed).
  - Install.ps1
        Convenience installer (same as double-clicking the .msix).
  - *.release-manifest.json
        Audit record: hashes, signer, timestamp. Not needed to install.

------------------------------------------------------------
 INSTALL
------------------------------------------------------------
  Easiest:
    Double-click the .msix and choose Install.

  Or (scripted):
    Right-click Install.ps1 -> "Run with PowerShell"
      (or:  powershell -ExecutionPolicy Bypass -File .\Install.ps1 )

  Then launch "Verifone Commander Price Book Manager" from the Start menu.

------------------------------------------------------------
 FIRST RUN - connecting to the Commander controller
------------------------------------------------------------
  Verifone Commander controllers use a self-signed / IP-only TLS
  certificate. In the app:
    Settings -> check "Allow untrusted / self-signed certificates"
  then enter the controller hostname/IP on the Account page and log in.

  (This in-app setting concerns the CONNECTION to the Commander server -
  it is unrelated to the app package's own signature.)

------------------------------------------------------------
 UPDATING
------------------------------------------------------------
  Install a newer version the same way (double-click the new .msix or run
  Install.ps1 again).

------------------------------------------------------------
 UNINSTALL
------------------------------------------------------------
  Settings -> Apps  ->  "Verifone Commander Price Book Manager"  -> Uninstall
  or PowerShell:
    Get-AppxPackage -Name 379b371a-43e8-4eed-8893-26a9ce5243c3 | Remove-AppxPackage
