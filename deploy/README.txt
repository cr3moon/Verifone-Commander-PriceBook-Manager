============================================================
 Verifone Commander Price Book Manager - Install Package
============================================================

WHAT THIS IS
  A self-contained Windows app (MSIX). The .NET 8 runtime and the
  Windows App SDK 2.1 runtime are bundled inside the package, so the
  target PC needs NO other downloads or runtime installs.

REQUIREMENTS
  - Windows 10 version 2004 (build 19041) or newer, 64-bit
  - That's it. No .NET install, no Windows App Runtime install.

FILES IN THIS FOLDER
  - VerifoneCommander.PriceBookManager.DesktopApp_<version>_x64.msix
        The application package (~83 MB; ~209 MB once installed).
  - PriceBookManager-DevCert.cer
        The PUBLIC signing certificate. It must be trusted on the PC so
        Windows will allow the package to install. (No private key here.)
  - Install.ps1
        Installer: trusts the certificate (one-time, admin) then installs
        the app for the current user.

------------------------------------------------------------
 INSTALL  (easy way)
------------------------------------------------------------
  1. Copy this whole folder to the target PC.
  2. Right-click Install.ps1  ->  "Run with PowerShell".
       (or:  powershell -ExecutionPolicy Bypass -File .\Install.ps1 )
  3. Approve the one Administrator prompt (that's the certificate trust).
  4. Launch "Verifone Commander Price Book Manager" from the Start menu.

------------------------------------------------------------
 INSTALL  (manual way)
------------------------------------------------------------
  1. Trust the signing cert  (one-time, in an *Administrator* PowerShell):
       Import-Certificate -FilePath .\PriceBookManager-DevCert.cer `
         -CertStoreLocation Cert:\LocalMachine\TrustedPeople

  2. Install the app  (normal PowerShell, no admin):
       Add-AppxPackage -Path .\VerifoneCommander.PriceBookManager.DesktopApp_<version>_x64.msix

------------------------------------------------------------
 FIRST RUN - connecting to the Commander controller
------------------------------------------------------------
  Verifone Commander controllers use a self-signed / IP-only TLS
  certificate. In the app:
    Settings -> check "Allow untrusted / self-signed certificates"
  then enter the controller hostname/IP on the Account page and log in.

  NOTE: this in-app setting is SEPARATE from the certificate trust above.
    - PriceBookManager-DevCert.cer  = lets Windows INSTALL the app.
    - "Allow untrusted certificates" = lets the app CONNECT to the
      Commander server despite its self-signed cert.

------------------------------------------------------------
 UPDATING
------------------------------------------------------------
  To install a newer version, just run Install.ps1 again (or Add-AppxPackage
  the new .msix). The certificate only needs trusting once.

------------------------------------------------------------
 UNINSTALL
------------------------------------------------------------
  Settings -> Apps  ->  "Verifone Commander Price Book Manager"  -> Uninstall
  or PowerShell:
    Get-AppxPackage -Name 379b371a-43e8-4eed-8893-26a9ce5243c3 | Remove-AppxPackage
