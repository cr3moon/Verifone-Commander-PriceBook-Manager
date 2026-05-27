# Verifone Commander Price Book Manager

WinUI 3 / .NET 8 Windows desktop app for managing the **price book (PLUs)** on a
Verifone Commander POS controller over its **live Sapphire HTTP/NAXML API**
(not files). Edit/search/delete PLUs, view departments/tax rates/age validations.

A rebranded **fork** (MIT) of upstream `Shubham Gogna / VerifoneCommander.PriceBookManager`.
Distributed by **Exact Technology Partners** (legal entity Futurevest ETP, LLC).

---

## Current state (2026-05-27)

- **Branch:** `feature/bulk-and-live-catalog` — cross-app feature merge **implemented** (4 stages,
  pushed). PR #1 (.NET 8 migration) is **merged to `main`**.
- The .NET 8 / WinApp SDK 2.1.3 base is migrated, rebranded, self-contained, **production-signed**.
- Builds clean (only pre-existing `SA1638` filename-case + MSIX signing-cert warnings); **Core.Tests 68/68**.
- ⚠️ **Not yet runtime-verified**: the new UI (Items grid, bulk delete, import) compiles and the pure
  logic is unit-tested, but the WinUI pages have not been exercised against a live controller or in
  mock mode. Manual run recommended before release (see "Verifying the new features").

## What's next — verify, then merge

The cross-app feature merge is code-complete on `feature/bulk-and-live-catalog`. Remaining:
1. **Runtime-verify** the new pages (below), ideally with `Settings.UseMocks` and then a live Commander.
2. Open/merge a PR to `main`; produce a fresh signed MSIX (see signing notes) for release.

Implemented this round (from sibling app `verifone-commander-import-export`):
- **Barcode logic** — `UpcUtilities` ported into `Core` (GTIN-14, H2-safe), wired into Edit + import.
- **Live Items page** — full PLU catalog from cache, filter by department / text / flags.
- **Bulk delete** — multi-select on the Items page → confirm dialog → live `DeletePriceLookUpAsync`.
- **Bulk import** — fixed-template CSV → preview/validate → live `uPLUs`.

Full brief + recon: `docs/feature-merge/cross-app-merge-brief.md`.

## Verifying the new features

- Run with mocks: set `UseMocks` (Settings) so `MockSapphireClient` supplies PLUs/departments, log in,
  then exercise **Items** (filter, select, Delete selected → confirm) and **Import** (a small CSV with
  header `upc,modifier,description,department,price`).
- Edit page now accepts UPC-A/EAN-13/GTIN-14 (1–14 digits), shows the canonical GTIN-14, and warns
  (non-blocking) on risky number systems / bad check digits — it no longer appends a check digit to a
  pasted 12-digit UPC-A.

---

## Architecture

| Project | Role |
|---|---|
| `src/Core` | `SapphireClient` (HTTP/NAXML to Commander), models, `HttpClientHttpRequestSender`, credential provider. `UpcUtilities` (UPC/GTIN-14 classify/normalize, H2-safe). `Import/` (`CsvReader` + `PluImportParser`). No UI. |
| `src/DesktopApp` | WinUI 3 + CommunityToolkit.Mvvm. `CachingSapphireClient`, pages (Account/Search/**Items**/Edit/BulkOperations/**Import**/Settings), MSIX packaging. |
| `src/Console` | Diagnostic harness (file-less, hits live API). |
| `src/Core.Tests` | xUnit over `Ean13Helper`, `ModelConverter`, `UpcUtilities`, `CsvReader`, `PluImportParser` (68 tests). |

- **Live API:** `SapphireClient` posts `cmd=validate` (login → cookie), `cmd=vPLUs`/`uPLUs`, `cmd=vposcfg`, etc. **PLU delete** = `uPLUs` body with a `<deletePLU><upc/><upcModifier/></deletePLU>` element, keyed on **EAN-13 + 3-digit modifier**.
- **MVVM:** parent `MainNavigationVm` holds one shared `Settings` instance, passed to page VMs; settings persist to `settings.json` on window close.

## Key decisions

- **2026-05-27 cross-app merge — build on THIS app, copy-port the logic:** harvested the sibling
  app's pure logic by **copying into `Core`** (not a shared netstandard2.0 lib — the .NET 4.8 sibling
  is a frozen source, not co-developed). The sibling's WinForms UI was **rebuilt in WinUI**, not ported.
- **2026-05-27 GTIN-14 is the canonical PLU key:** the stored `long Ean13` is the numeric GTIN-14
  (the wire already writes `upc` as `D14`). Edit + import accept UPC-A/EAN-13/GTIN-14 and **never
  recompute a check digit on a complete barcode** (the "H2" guard in `UpcUtilities`). The old
  Edit-page behavior (append a check digit to a 12-digit entry) was a footgun and was removed.
- **2026-05-27 import = fixed-template CSV (v1):** columns matched by header name
  (`upc,modifier,description,department,price`); column-mapping UX deferred. Live deletes/imports
  always require an explicit confirmation dialog.
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

- `docs/feature-merge/cross-app-merge-brief.md` — **IMPLEMENTED** on `feature/bulk-and-live-catalog`: cross-app analysis + feature targets + recon of the other app.
- `docs/migration/dot-net-8-migration.md` — .NET 8 migration plan (**IMPLEMENTED** on this branch).
- `docs/migration/Porting a .NET 6 ... .NET 10 LTS.md` — future .NET 10 LTS research (deferred).
- `docs/security/network-security-review.md` — network/TLS review (the cert-bypass concern; addressed via the user toggle).

## Related repos

- `../verifone-commander-import-export` — the sibling app (.NET 4.8 WinForms, file-based) we're harvesting features from.
- `../azure-code-signing` — signing infra; its `Resolve-SignToolPath.ps1` MSIX fix is in that repo's PR #1.
