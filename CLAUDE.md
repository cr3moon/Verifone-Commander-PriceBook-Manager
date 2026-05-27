# Verifone Commander Price Book Manager

WinUI 3 / .NET 8 Windows desktop app for managing the **price book (PLUs)** on a
Verifone Commander POS controller over its **live Sapphire HTTP/NAXML API**
(not files). Edit/search/delete PLUs, view departments/tax rates/age validations.

A rebranded **fork** (MIT) of upstream `Shubham Gogna / VerifoneCommander.PriceBookManager`.
Distributed by **Exact Technology Partners** (legal entity Futurevest ETP, LLC).

---

## Current state (2026-05-26)

- **Branch:** `migrate/net8-winappsdk-2.1` — **PR #1 OPEN** (awaiting user merge to `main`).
- Fully migrated, rebranded, self-contained, and **production-signed**. Builds clean; Core.Tests 9/9.
- Deliverable (signed, no-prereq MSIX) staged at `C:\dev\PriceBookManager-Deploy\`.
- Installed + verified on this machine running on the bundled 2.1.3 runtime.

## What's next — cross-app feature merge (the reason for the next session)

Bring capability into THIS app from the sibling app `verifone-commander-import-export`:
**bulk import, stronger barcode handling on ingest, a live item view filterable by
department/fields (from the live system, not files), bulk PLU delete**, possibly more.
Start with an **analysis** of the most efficient approach (build-on-which / port-vs-rebuild),
then implement. **Full brief + recon: `docs/feature-merge/cross-app-merge-brief.md`.**

Branch plan: after PR #1 merges to `main`, branch from `main` (suggested
`feature/bulk-and-live-catalog`) for the implementation. The analysis itself is read-only.

---

## Architecture

| Project | Role |
|---|---|
| `src/Core` | `SapphireClient` (HTTP/NAXML to Commander), models, `HttpClientHttpRequestSender`, credential provider. No UI. |
| `src/DesktopApp` | WinUI 3 + CommunityToolkit.Mvvm. `CachingSapphireClient`, pages (Account/Search/Edit/BulkOperations/Settings), MSIX packaging. |
| `src/Console` | Diagnostic harness (file-less, hits live API). |
| `src/Core.Tests` | xUnit over `Ean13Helper` + `ModelConverter`. |

- **Live API:** `SapphireClient` posts `cmd=validate` (login → cookie), `cmd=vPLUs`/`uPLUs`, `cmd=vposcfg`, etc. **PLU delete** = `uPLUs` body with a `<deletePLU><upc/><upcModifier/></deletePLU>` element, keyed on **EAN-13 + 3-digit modifier**.
- **MVVM:** parent `MainNavigationVm` holds one shared `Settings` instance, passed to page VMs; settings persist to `settings.json` on window close.

## Key decisions

- **2026-05-26 .NET 8, not 10 (for now):** staying on .NET 8 LTS this round; **.NET 10 LTS bump deferred** (research in `docs/migration/`). .NET 8 EOL ~Nov 2026, so 10 is the eventual target.
- **WinApp SDK 2.1.3** (stable; 1.x EOL). Dropped `CommunityToolkit.WinUI` (no 2.x build); inlined its one `EnqueueAsync` use.
- **Self-contained MSIX** (`SelfContained` + `WindowsAppSDKSelfContained`): bundles .NET 8 + WinApp 2.1 runtimes → customer installs nothing else. ~83 MB package / ~209 MB installed.
- **TLS bypass is a user toggle, secure by default** (`Settings.AllowUntrustedCertificates`): needed for the Commander's self-signed/IP-only cert. Separate from the app's own code signature.
- **Production signing:** Azure Artifact Signing, cert subject `Futurevest ETP, LLC` (publicly trusted → no cert import on customer PC). MSIX `Identity/Publisher` MUST equal the cert subject DN.
- **Rebrand is legitimate:** MIT fork; upstream `LICENSE` retained + `NOTICE` added.

## Technical notes / gotchas

- **Build from WSL via `/wincmd`** (`cd /mnt/c && cmd.exe /c "cd /d C:\... && dotnet ..."`). Calling `.exe` directly from a Linux CWD → "Invalid argument".
- **Self-contained bundling only happens at publish/packaging**, NOT plain `dotnet build`. Build the deployable MSIX with:
  `dotnet msbuild src\DesktopApp\...csproj /restore /p:Configuration=Release /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Never`
- **MSIX signing needs an MSIX-capable signtool** (`appxpackaging.dll` co-located) — the `Microsoft.Windows.SDK.BuildTools` 28000 one, NOT the Windows Kits 26100 one. Fixed in the `azure-code-signing` repo's resolver (its PR #1).
- **Sign:** `~/bin/codesign "<file>" "Exact Technology Partners - <Product> <Version> (Production)"` (the `/signing-publisher` skill wraps this).
- **.NET 8 dropped the `win10-x64` RID** → use `win-x64`.
- **Package identity:** Name `379b371a-...` (GUID, stable); PackageFamilyName changed when Publisher changed to the Futurevest DN.

## Documentation index

- `docs/feature-merge/cross-app-merge-brief.md` — **NEXT WORK**: cross-app analysis + feature targets + recon of the other app.
- `docs/migration/dot-net-8-migration.md` — .NET 8 migration plan (**IMPLEMENTED** on this branch).
- `docs/migration/Porting a .NET 6 ... .NET 10 LTS.md` — future .NET 10 LTS research (deferred).
- `docs/security/network-security-review.md` — network/TLS review (the cert-bypass concern; addressed via the user toggle).

## Related repos

- `../verifone-commander-import-export` — the sibling app (.NET 4.8 WinForms, file-based) we're harvesting features from.
- `../azure-code-signing` — signing infra; its `Resolve-SignToolPath.ps1` MSIX fix is in that repo's PR #1.
