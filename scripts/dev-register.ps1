# Dev-mode deploy: registers the loose (unsigned, unpackaged) Release build layout
# so the app can be run and iterated on WITHOUT signing or MSIX packaging.
# Re-run after every rebuild — registration points at the bin folder, so a plain
# re-register picks up new binaries. Requires Windows Developer Mode.
$ErrorActionPreference = 'Stop'

$manifest = 'C:\dev\projects\github\Verifone-Commander-PriceBook-Manager\src\DesktopApp\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppxManifest.xml'
$pkgName = '379b371a-43e8-4eed-8893-26a9ce5243c3'

try {
    Add-AppxPackage -Register $manifest
}
catch {
    # Same identity already installed from a signed MSIX — replace it with the
    # dev registration (app settings are lost; reinstalling the signed MSIX
    # later restores normal installs).
    $installed = Get-AppxPackage -Name $pkgName
    if ($installed) {
        Write-Host "Register blocked by installed $($installed.Version) (signed=$(-not $installed.IsDevelopmentMode)); removing and retrying..."
        Remove-AppxPackage -Package $installed.PackageFullName
        Add-AppxPackage -Register $manifest
    }
    else {
        throw
    }
}

$now = Get-AppxPackage -Name $pkgName
Write-Host "Registered $($now.Version) DevMode=$($now.IsDevelopmentMode)"
Write-Host "Location: $($now.InstallLocation)"

# Launch it.
Start-Process "shell:AppsFolder\$($now.PackageFamilyName)!App"
Write-Host 'Launched.'
