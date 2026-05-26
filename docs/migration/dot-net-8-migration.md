## Final migration research: .NET 6 → .NET 8 for this project

This project should be migrated from **.NET 6 to .NET 8** because .NET 6 is now out of support. Microsoft’s .NET support policy lists .NET 6 end of support as **November 12, 2024**; once a .NET version is out of support, Microsoft no longer provides security updates or technical support for it. ([Microsoft][1])

For this repository, the migration is mostly a **project-file, package, tooling, and validation effort**, not a large application rewrite. The uploaded inventory shows four projects:

| Project      | Current target               | Migration target             |
| ------------ | ---------------------------- | ---------------------------- |
| `Core`       | `net6.0`                     | `net8.0`                     |
| `Console`    | `net6.0`                     | `net8.0`                     |
| `Core.Tests` | `net6.0`                     | `net8.0`                     |
| `DesktopApp` | `net6.0-windows10.0.19041.0` | `net8.0-windows10.0.19041.0` |

The `DesktopApp` is the highest-risk part because it uses **WinUI 3**, **Windows App SDK**, **MSIX packaging**, package signing, `RuntimeIdentifiers`, app manifests, and GitHub Actions packaging. The Core/Console/Test projects use ordinary .NET APIs and should be comparatively low-risk. 

---

# Recommended final target

Use:

```xml
<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
```

For the WinUI 3 / Windows App SDK desktop app, use the current stable Windows App SDK line:

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.1.3" />
```

As of Microsoft’s current Windows App SDK release information, **Windows App SDK 2.1.3** is the latest stable servicing release on the 2.x line. ([GitHub][2]) The stable channel is the correct production channel; Preview and Experimental channels are not the right target for this migration. Microsoft’s release-channel documentation distinguishes Stable, Preview, and Experimental channels for Windows App SDK releases. ([Microsoft Learn][3])

However, because this project is currently on **Windows App SDK 1.4.230913002**, I would stage the migration rather than combine every change into one commit. Windows App SDK 2.0 is the first major-version increment since Windows App SDK 1.0 and uses a new semantic-versioning scheme, so a move from 1.4 to 2.1.3 should be treated as a major dependency upgrade. ([Microsoft Learn][4])

Recommended sequence:

```text
Step 1:
.NET 6 + Windows App SDK 1.4
        ↓
.NET 8 + Windows App SDK 1.8.x

Step 2:
.NET 8 + Windows App SDK 1.8.x
        ↓
.NET 8 + Windows App SDK 2.1.3
```

If this is an internal app and you are comfortable debugging build/package issues immediately, you can go directly to **Windows App SDK 2.1.3**. For a production app with MSIX packaging and CI signing, staging the migration is safer.

---

# Required project-file changes

## 1. Retarget Core, Console, and Core.Tests

Change:

```xml
<TargetFramework>net6.0</TargetFramework>
```

to:

```xml
<TargetFramework>net8.0</TargetFramework>
```

Affected projects:

```text
src/Core/
src/Console/
src/Core.Tests/
```

## 2. Retarget DesktopApp

Change:

```xml
<TargetFramework>net6.0-windows10.0.19041.0</TargetFramework>
```

to:

```xml
<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
```

Keep the Windows-specific TFM. This app uses WinUI 3 / Windows App SDK APIs, so `net8.0` alone is not appropriate for the desktop project. The existing `windows10.0.19041.0` target is conservative and compatible with modern Windows 10/11 API projection behavior.

## 3. Add or verify TargetPlatformMinVersion

For the DesktopApp:

```xml
<TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
```

Your current inventory indicates the app manifest already targets modern Windows and the MSIX manifest has a minimum OS version of `10.0.17763.0`.  Decide whether you want to keep the install minimum at `17763` or align it with `19041`. For a Windows 11-focused migration, `19041` is a clean conservative baseline.

## 4. Replace old RuntimeIdentifier

The inventory shows the DesktopApp currently uses:

```xml
<RuntimeIdentifiers>win10-x64</RuntimeIdentifiers>
```

Change that to:

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

or, if you need multiple architectures:

```xml
<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
```

For this project, because the existing DesktopApp is already configured around `Platforms=x64`, the minimal migration change is:

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

---

# Windows App SDK version recommendation

## Final target

Use:

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.1.3" />
```

This is the recommended final state for a .NET 8 WinUI 3 app on Windows 11. The codebase uses standard WinUI 3 surfaces: `Application`, `Window`, `Page`, `UserControl`, `NavigationView`, `Frame.Navigate`, `Flyout`, `Symbol`, `InfoBarSeverity`, `IValueConverter`, `DispatcherQueue`, `ApplicationData`, and `Launcher.LaunchFolderPathAsync`. There is no indication in the uploaded inventory that this project depends on preview-only APIs, advanced composition/windowing APIs, or fragile Windows App SDK internals. 

## Temporary staging/fallback target

Use latest **1.8.x** only as a temporary bridge or fallback:

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.x" />
```

That is useful if you want to isolate the .NET 8 retargeting from the Windows App SDK major-version upgrade. Microsoft’s Windows App SDK support documentation lists supported Windows releases by SDK line and should be checked when choosing the exact supported line. ([Microsoft Learn][5])

## Avoid

Do not stay on:

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.4.230913002" />
```

That version is too old for the intended modernization path. Also avoid Preview or Experimental packages unless you are validating a specific bug fix.

---

# Package updates required or recommended

The inventory lists these important package areas: `Microsoft.WindowsAppSDK`, `CommunityToolkit.Mvvm`, `CommunityToolkit.WinUI`, `Microsoft.Extensions.Logging.*`, `Newtonsoft.Json`, `Serilog.Extensions.Logging.File`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.Windows.SDK.BuildTools`, and `StyleCop.Analyzers`. 

Recommended update approach:

```bash
dotnet list src/VerifoneCommander.PriceBookManager.sln package --outdated
```

Then update in controlled groups:

## Runtime/framework packages

Update Microsoft.Extensions packages to versions aligned with .NET 8:

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.x.x" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.x.x" />
```

## WinUI / Windows App SDK packages

Final target:

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.1.3" />
```

Also update:

```xml
<PackageReference Include="CommunityToolkit.Mvvm" Version="latest stable" />
<PackageReference Include="CommunityToolkit.WinUI" Version="latest compatible stable" />
```

This matters because the app uses MVVM Toolkit source generators, `ObservableObject`, `[ObservableProperty]`, `[NotifyPropertyChangedFor]`, `[NotifyCanExecuteChangedFor]`, `RelayCommand`, `AsyncRelayCommand`, `IMessenger`, and `DispatcherQueue.EnqueueAsync`. 

## Test packages

Update:

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="latest stable" />
<PackageReference Include="xunit" Version="latest stable" />
<PackageReference Include="xunit.runner.visualstudio" Version="latest stable" />
```

The existing test project is low-risk: it uses xUnit tests for `Ean13Helper` and `ModelConverter`. 

## Packages that can probably stay initially

These do not need to be changed as part of the first migration pass unless `dotnet list package --outdated` or build errors indicate otherwise:

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="Serilog.Extensions.Logging.File" Version="3.0.0" />
<PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.507" />
```

`Newtonsoft.Json` can be replaced later with `System.Text.Json`, but that is optional modernization, not required for .NET 8.

---

# CI / GitHub Actions changes

The inventory shows the GitHub Actions workflow uses `actions/setup-dotnet@v1` with `6.0.x`, builds on `windows-latest`, uses MSBuild, restores, packages, signs with a PFX secret, and uploads an MSIX artifact. 

Change:

```yaml
- uses: actions/setup-dotnet@v1
  with:
    dotnet-version: '6.0.x'
```

to:

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
```

Also verify the runner has compatible Visual Studio Build Tools, MSBuild, MSIX packaging tools, and the Windows SDK needed by the DesktopApp.

The packaging command can likely remain structurally similar, but the build must be revalidated after the `RuntimeIdentifier` and Windows App SDK changes.

---

# Build tooling requirements

Install or verify:

```bash
dotnet --list-sdks
dotnet --info
```

You need:

```text
.NET 8 SDK
Visual Studio 2022 / Build Tools 2022 compatible with .NET 8
Windows App SDK tooling
Windows SDK
MSIX Packaging Tools
```

Microsoft documents that SDKs have Visual Studio/MSBuild minimum version requirements; older Visual Studio/MSBuild versions may not load or build newer SDKs correctly. ([Microsoft][1])

---

# Build and validation commands

Run these after retargeting:

```bash
dotnet restore src/VerifoneCommander.PriceBookManager.sln
dotnet build src/VerifoneCommander.PriceBookManager.sln -c Release
dotnet test src/VerifoneCommander.PriceBookManager.sln -c Release
```

Then validate the WinUI/MSIX path:

```bash
msbuild src/DesktopApp/DesktopApp.csproj /restore /p:Configuration=Release /p:Platform=x64
```

Validate both app launch modes because the inventory shows both packaged and unpackaged launch profiles:

```text
MsixPackage
Project / unpackaged
```

Microsoft’s Windows App SDK deployment guidance distinguishes packaged and unpackaged deployment behavior, so both profiles need to be tested. ([Microsoft Learn][3])

---

# Expected risk areas

| Area                     |        Risk | Why                                                                                     |
| ------------------------ | ----------: | --------------------------------------------------------------------------------------- |
| `Core` project           |         Low | Uses standard .NET APIs: HTTP, XML, LINQ, async/await, models.                          |
| `Console` project        |         Low | Simple diagnostic harness using logging and Core services.                              |
| `Core.Tests`             |         Low | xUnit tests over helper/model-conversion code.                                          |
| `DesktopApp` source code |  Low/Medium | Standard WinUI 3 controls and MVVM Toolkit usage.                                       |
| Windows App SDK upgrade  |      Medium | Current version is 1.4; final target is 2.1.3, a major-line change.                     |
| MSIX packaging           | Medium/High | Package signing, manifests, runtime identifiers, and CI packaging need validation.      |
| CI pipeline              |      Medium | Needs .NET 8 SDK and compatible MSBuild/Windows SDK tooling.                            |
| Analyzer behavior        |      Medium | Shared props use `AnalysisLevel=latest`; a newer SDK may surface new analyzer warnings. |

---

# Code-level concerns to inspect

The inventory does not show obvious removed APIs or .NET 8-incompatible source patterns. Still, inspect these areas during migration:

## 1. Analyzer changes

The shared `.config/Analyzers.props` enables:

```xml
<EnableNETAnalyzers>true</EnableNETAnalyzers>
<AnalysisLevel>latest</AnalysisLevel>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

Because `AnalysisLevel=latest` floats with the SDK, moving from .NET 6 SDK to .NET 8 SDK may introduce new warnings. If warnings break CI, either fix them or pin the analysis level:

```xml
<AnalysisLevel>8.0</AnalysisLevel>
```

## 2. Certificate validation bypass

The Core HTTP layer uses `HttpClientHandler.ServerCertificateCustomValidationCallback` and accepts certificates broadly for POS communication.  This may be intentional because Verifone Commander devices often use local/self-signed certs, but it should be retested and documented during migration.

## 3. MVVM Toolkit source generation

The app uses generated properties and partial methods from CommunityToolkit.Mvvm.  After updating packages, check generated-property behavior, command `CanExecute` behavior, and any analyzer/source-generator warnings.

## 4. WinUI packaged/unpackaged behavior

The app uses `ApplicationData.Current.LocalFolder.Path`, app-data settings, file logging, `Launcher.LaunchFolderPathAsync`, and MSIX packaging.  Verify that settings and logs still land in the expected location in both packaged and unpackaged runs.

---

# Functional validation checklist

After the migration, test:

```text
Application starts packaged
Application starts unpackaged
Settings file loads and saves
App-data folder opens from Settings page
Logging writes log-{Date}.txt
Mock mode still works
Login/logout flow still works
Sapphire credential caching still works
PLU search works
PLU edit/save/delete works
Department/tax/age-validation lookup works
Bulk operations work
XML backup generation works
Core.Tests pass
MSIX package builds
MSIX package signs
MSIX installs on Windows 11
GitHub Actions build produces artifact
```

---

# Suggested migration patch

## Core / Console / Tests

```diff
- <TargetFramework>net6.0</TargetFramework>
+ <TargetFramework>net8.0</TargetFramework>
```

## DesktopApp

```diff
- <TargetFramework>net6.0-windows10.0.19041.0</TargetFramework>
+ <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>

+ <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>

- <RuntimeIdentifiers>win10-x64</RuntimeIdentifiers>
+ <RuntimeIdentifier>win-x64</RuntimeIdentifier>

- <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.4.230913002" />
+ <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.1.3" />
```

If staging the migration, use latest stable `1.8.x` before moving to `2.1.3`.

## GitHub Actions

```diff
- uses: actions/setup-dotnet@v1
+ uses: actions/setup-dotnet@v4
  with:
-   dotnet-version: '6.0.x'
+   dotnet-version: '8.0.x'
```

---

# Optional modernization after the migration

Do these after the .NET 8 migration is stable, not in the same commit unless you want a larger refactor.

| Change                                                               | Recommendation                                       |
| -------------------------------------------------------------------- | ---------------------------------------------------- |
| Enable nullable reference types                                      | Good, but expect many warnings.                      |
| Replace Newtonsoft.Json with `System.Text.Json`                      | Optional; not required.                              |
| Replace sync-over-async `Program.Main` with `static async Task Main` | Low-risk cleanup.                                    |
| Introduce `Directory.Packages.props`                                 | Useful for package governance.                       |
| Pin `AnalysisLevel` to `8.0`                                         | Improves build reproducibility.                      |
| Add dependency injection / generic host                              | Optional architectural improvement.                  |
| Harden certificate validation                                        | Worth reviewing, but may need POS-specific behavior. |

---

# Bottom-line recommendation

Migrate the solution to:

```xml
<TargetFramework>net8.0</TargetFramework>
```

for `Core`, `Console`, and `Core.Tests`, and:

```xml
<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.1.3" />
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

for `DesktopApp`.

The application code itself does not appear to require a major rewrite. The migration work is primarily:

```text
TFM retargeting
Windows App SDK upgrade
package updates
RID cleanup
CI update
MSIX packaging validation
WinUI packaged/unpackaged runtime testing
```

The main technical risk is **not .NET 8 compatibility in the Core code**. The main risk is **Windows App SDK + WinUI + MSIX packaging + CI tooling**.

[1]: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core?utm_source=chatgpt.com ".NET and .NET Core official support policy | .NET"
[2]: https://github.com/microsoft/WindowsAppSDK/releases?utm_source=chatgpt.com "Releases · microsoft/WindowsAppSDK - GitHub"
[3]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels?utm_source=chatgpt.com "Windows App SDK release channels - Windows apps | Microsoft Learn"
[4]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0?utm_source=chatgpt.com "Windows App SDK 2.0 release notes - Windows apps"
[5]: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/support?utm_source=chatgpt.com "Windows App SDK and supported Windows releases"

