# Verifone Commander Price Book Manager

WinUI 3 / .NET 8 Windows desktop app for managing the **price book (PLUs)** on a
Verifone Commander POS controller over its **live Sapphire HTTP/NAXML API**
(not files). Edit / search / delete / **bulk-delete / bulk-import / audit** PLUs;
view departments / tax rates / age validations.

A rebranded **fork** (MIT) of upstream `Shubham Gogna / VerifoneCommander.PriceBookManager`.
Distributed by **Exact Technology Partners** (legal entity Futurevest ETP, LLC).

---

## Current state (2026-06-03)

- **Branch:** `feature/bulk-and-live-catalog` (PR #2 OPEN against `main`).
- All 4 stages of the cross-app feature merge are implemented **and runtime-verified by the operator** —
  Items grid, bulk delete, bulk CSV import, edit-page reconciliation. Plus 4 substantial post-RC
  iterations on UX (mock-mode usability, severity colors, dropdown filters with counts, possible-duplicate
  detection). The user is on **1.1.4.0 RC** as of this checkpoint and just finished testing 1.1.3.0.
- Build clean (only pre-existing `SA1638` + MSIX signing-cert warnings); **Core.Tests 74/74**.
- **Currently in the deploy kit:** `1.1.1.0` Production-signed (customer-deliverable).
- **Currently being iterated on:** `1.1.4.0` RC (in `src/DesktopApp/AppPackages/.../`).

## What's next

1. **Validate 1.1.4.0 RC** (severity colors, UPC status filter dropdown, possible-duplicate detection).
2. Once user signs off, **re-sign 1.1.4.0 as Production** and refresh
   `C:\dev\PriceBookManager-Deploy\` (the customer hand-off folder).
3. After that, merge **PR #2** to `main` and tag `v1.1.4.0`.
4. Open follow-ups (not blocking the merge):
   - **Deactivate-and-recreate workflow** (Stage C from the prior plan): for a non-scannable
     PLU, create a corrected duplicate + set the old to Not Sold (flag sysid 2). Operator workflow
     proven manually in production; UI not yet built.

## Verifying the new features

Mock mode auto-logs in (any non-empty credentials work); switching mock ↔ live now happens
**at runtime via Settings** (no restart required — the `Switchable*` classes route at call time).

Areas to exercise:
- **Items page** — Sold / Not Sold / All dropdown counts; UPC status dropdown counts;
  severity-colored Status column; multi-select bulk delete; refresh.
- **Edit page** — UPC field accepts UPC-A / EAN-13 / GTIN-14 (1–14 digits); live severity-colored
  bold status under the EAN field updates as you type; save still warns on risky codes.
- **Import page** — fixed-template CSV (`upc,modifier,description,department,price`);
  preview shows per-row Valid / Warning / Error; case-insensitive dept matching.
- **Possible duplicates** — toggle in Items filter bar. Flagged rows get a bold blue 2px border;
  tap a flagged row → right-side detail panel; tap anywhere outside any row → panel + borders clear.

---

## Architecture

| Project | Role |
|---|---|
| `src/Core` | `SapphireClient` (HTTP/NAXML to Commander), models, `HttpClientHttpRequestSender`, credential provider. `UpcUtilities` (UPC/GTIN-14 classify / normalize / **severity** / **issue label**, H2-safe). `Import/` (`CsvReader` + `PluImportParser`). No UI. |
| `src/DesktopApp` | WinUI 3 + CommunityToolkit.Mvvm. `CachingSapphireClient`, **`SwitchableSapphireClient` + `SwitchableSapphireCredentialsProvider`** for runtime mock/live routing, pages (Account / Search / **Items** / Edit / BulkOperations / **Import** / Settings), MSIX packaging. Converters: `InfoBarSeverityConverter`, `IntToSymbolConverter`, `StringFormatConverter`, **`UpcSeverityToBrushConverter`**, **`BoolToBorderThicknessConverter`**. |
| `src/Console` | Diagnostic harness (file-less, hits live API). |
| `src/Core.Tests` | xUnit over `Ean13Helper`, `ModelConverter`, **`UpcUtilities`** (incl. `GetIssueLabel`, `GetSeverity`), `CsvReader`, `PluImportParser` (**74 tests**). |

- **Live API:** `SapphireClient` posts `cmd=validate` (login → cookie), `cmd=vPLUs` / `uPLUs`, `cmd=vposcfg`, etc.
  PLU delete = `uPLUs` body with `<deletePLU><upc/><upcModifier/></deletePLU>`, keyed on **EAN-13 + 3-digit modifier**.
  **Setting a PLU to Not Sold** = `uPLUs` with `<flags><domain:flag sysid="2"/></flags>` on the PLU element.
- **MVVM:** parent `MainNavigationVm` holds one shared `Settings` instance, passed to page VMs;
  settings persist to `settings.json` on window close. `LoginStateChangedMessage` + `LoadProductForEditMessage` +
  **`ModeChangedMessage`** flow via `WeakReferenceMessenger.Default`.

## Key decisions

- **2026-06-03 severity buckets + color-coding:** new `UpcUtilities.UpcSeverity` (None / Info / Warning / Error)
  drives a single converter (`UpcSeverityToBrushConverter`) used by Items + Edit. **Error** = bad check digit,
  Random-weight (NS=2), Coupon (NS=5/9). **Info** = NDC (NS=3), In-store (NS=4). Bad check on top of NDC/In-store
  escalates to Error.
- **2026-06-03 NotSold filter = dropdown with counts** (Sold / Not Sold / All); UPC issues filter likewise =
  dropdown (All / Valid / Any UPC issue / specific issue types). Each row stores a primary `UpcFilterKey` chosen
  by **risky-NS-wins-over-bad-check** so a "Random-weight + bad check" still appears under **Random-weight**.
- **2026-06-03 NotSold = flag sysid 2** (operator-confirmed via claude-mem memory `plu-not-sold-flag`, corroborated
  by HAR trace of ConfigClient saving a PLU as Not Sold). `Plu.IsNotSold => FlagIds.Contains(PluFlags.NotSold)`.
- **2026-06-03 possible-duplicate detection:** per-department token (≥4-char word) inverted index. Flagged rows
  get a blue 2px border; tap-driven right-side detail panel; tap-outside dismisses. Detection algorithm tokenizes
  on `[space - _ . , / \ ( ) tab]` — items in the same department sharing any ≥4-char uppercase word are flagged.
- **2026-05-27 mock/live runtime switching (refactored 2026-05-28):** UseMocks setting toggles the
  `SwitchableSapphireClient`/`...CredentialsProvider` route at call time. Toggling the Settings checkbox sends
  `ModeChangedMessage`; AccountPageVm ends the session and the next login uses the new backend. **Mock mode
  auto-logs in** (any non-empty creds work) via the AccountPage `Loaded` hook → `EnsureMockSessionAsync`.
- **2026-05-27 cross-app merge — build on THIS app, copy-port the logic:** harvested the sibling app's pure
  logic by copying into `Core` (not a shared netstandard2.0 lib — the sibling is .NET 4.8, frozen). The sibling's
  WinForms UI was rebuilt in WinUI, not ported.
- **2026-05-27 GTIN-14 is the canonical PLU key:** the stored `long Ean13` is the numeric GTIN-14 (the wire writes
  `<upc>` as `D14`). Edit + import accept UPC-A/EAN-13/GTIN-14 and **never recompute a check digit on a complete
  barcode** (the "H2" guard in `UpcUtilities`). The old Edit-page auto-append-check-digit behavior was a footgun
  and was removed.
- **2026-05-27 import = fixed-template CSV (v1):** columns matched by header name (case-insensitive); column-mapping
  UX deferred. Live deletes/imports always require an explicit confirmation dialog, and the dialog wording is
  **mode-aware** ("the LIVE POS controller" vs "MOCK data (no controller)") via `App.IsMockMode`.
- **2026-05-26 .NET 8, not 10 (for now):** staying on .NET 8 LTS this round; .NET 10 LTS bump deferred
  (research in `docs/migration/`). .NET 8 EOL ~Nov 2026, so 10 is the eventual target.
- **WinApp SDK 2.1.3** (stable; 1.x EOL). Dropped `CommunityToolkit.WinUI` (no 2.x build); inlined its one
  `EnqueueAsync` use.
- **Self-contained MSIX** (`SelfContained` + `WindowsAppSDKSelfContained`): bundles .NET 8 + WinApp 2.1 runtimes
  → customer installs nothing else. ~83 MB package / ~209 MB installed.
- **TLS bypass is a user toggle, secure by default** (`Settings.AllowUntrustedCertificates`): needed for
  Commander's self-signed/IP-only cert. Separate from the app's own code signature.
- **Production signing:** Azure Artifact Signing, cert subject `Futurevest ETP, LLC` (publicly trusted → no cert
  import on customer PC). MSIX `Identity/Publisher` MUST equal the cert subject DN.
- **Rebrand is legitimate:** MIT fork; upstream `LICENSE` retained + `NOTICE` added.

## Technical notes / gotchas

- **Build from WSL via `cmd.exe /c`** (`cd /mnt/c && cmd.exe /c "cd /d C:\... && dotnet ..."`). Calling `.exe`
  directly from a Linux CWD → "Invalid argument".
- **Self-contained bundling only happens at publish/packaging**, NOT plain `dotnet build`. Deployable MSIX:
  ```
  dotnet msbuild src\DesktopApp\VerifoneCommander.PriceBookManager.DesktopApp.csproj /restore
    /p:Configuration=Release /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true
    /p:AppxPackageSigningEnabled=false /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Never
  ```
- **Sign:** `~/bin/codesign "<file>" "Exact Technology Partners - <Product> <Version> (Production|RC|Test)"`
  (the `/signing-publisher` skill wraps this and validates PE metadata + tenant).
- **MSIX signing needs an MSIX-capable signtool** (`appxpackaging.dll` co-located) — the
  `Microsoft.Windows.SDK.BuildTools` 28000 one, NOT the Windows Kits 26100 one. Fixed in the
  `azure-code-signing` repo's resolver (its PR #1).
- **.NET 8 dropped the `win10-x64` RID** → use `win-x64`.
- **Package identity:** Name `379b371a-...` (GUID, stable); PackageFamilyName changed when Publisher changed
  to the Futurevest DN.
- **StyleCop quirks** that bit us this round:
  - SA1201 (ordering — enum vs class, field vs property/method) — separate-file or `#pragma` is cleanest.
  - SA1204 (static-before-instance) — local functions inside an instance method sidestep this.
  - SA1025 (multi-space alignment) and SA1512 (blank-after-comment) fire on aligned `// notes`. Collapse to single
    space and don't follow `// section` comments with a blank line.
  - CA1062 (validate args) and CA1822 (mark static) are **errors** in `.editorconfig`. CA2007 (ConfigureAwait) is
    too, but `IAsyncOperation<T>` has no `.ConfigureAwait` so it's exempt (use `await dialog.ShowAsync();`).
- **WinUI x:Bind to a member of a possibly-null path** (e.g. `ViewModel.SelectedDuplicateRow.PossibleDuplicates`)
  works — returns null when intermediate is null. Pair with a Visibility binding (we use `ShowDuplicateDetailsPanel`).
- **MSIX Update vs Install behavior:** higher Version installs cleanly over a lower one; same-version installs
  refuse. We bump the patch (`1.1.X.0`) for each RC iteration so the user can update in place.

## Tooling for live-data audits (not part of the app)

- `scripts/upc-audit.py` — anomaly report from a backup XML (BADCHK / SHORT / NS-risk), 3 views:
  counts summary, grouped by **department (names from sibling `poscfg.xml`)**, grouped by issue type.
- `scripts/sequential-pattern-audit.py` — hunts hand-entered UPCs (passing GTIN-14 but with patterns):
  HIGH confidence = sig<10 or gap=1; MEDIUM = tight cluster (gap≤5) or pair with shared first-word; LOW =
  looser cluster (5<gap≤100) where many are legitimate manufacturer SKU runs.
- `sample-data/backup-2026-06-03T09-23-38.xml` — current live snapshot (1,035 PLUs) used as the test fixture.
- `sample-data/anomalous-barcodes-2026-06-03.txt` — most recent anomaly report.
- `sample-data/likely-handentered-2026-06-03.txt` — most recent pattern report (HIGH: 51, MED: 124, LOW: 132).

## Documentation index

- `docs/feature-merge/cross-app-merge-brief.md` — **IMPLEMENTED** on `feature/bulk-and-live-catalog`: cross-app
  analysis + feature targets + recon of the other app.
- `docs/reference/upc-handling.md` — **AUTHORITATIVE**: AS-IS rule, H2-corruption story, GS1/GTIN-14 algorithm,
  NS-digit risk table, the fix-by-replacement (deactivate-and-recreate) workflow. Points at the sibling project's
  full research for the empirical evidence.
- `docs/migration/dot-net-8-migration.md` — .NET 8 migration plan (**IMPLEMENTED** on this branch).
- `docs/migration/Porting a .NET 6 ... .NET 10 LTS.md` — future .NET 10 LTS research (deferred).
- `docs/security/network-security-review.md` — network/TLS review (the cert-bypass concern; addressed via the
  user toggle).

## Related repos

- `../verifone-commander-import-export` — the sibling app (.NET 4.8 WinForms, file-based) we harvested features
  from. Has the **HAR traces** of ConfigClient operations and the **FULL_BACKUP** archives (FB7 / FB9 / FB13 / FB14)
  whose `poscfg.xml` is the source of dept-id → dept-name mapping in our audit scripts.
- `../azure-code-signing` — signing infra; its `Resolve-SignToolPath.ps1` MSIX fix is in that repo's PR #1.
- `~/.claude/projects/-mnt-c-dev-projects-github-verifone-commander-import-export/memory/` — claude-mem memories
  with the load-bearing facts (`plu-not-sold-flag`, `upc-converter-normalizer-spec`, `upc-is-literal-scan-code`).
</content>
</invoke>