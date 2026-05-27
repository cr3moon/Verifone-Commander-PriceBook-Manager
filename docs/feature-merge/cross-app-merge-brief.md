# Cross-App Feature Merge — Brief & Analysis Starting Point

**Status: PLANNING (created 2026-05-26).** Next session starts here.

## Goal

Bring these capabilities into **this** app (Verifone Commander Price Book Manager —
WinUI 3 / .NET 8 / live Sapphire API), drawing on the sibling app
`verifone-commander-import-export`:

1. **Bulk import** of PLUs/items.
2. **Stronger barcode handling** when ingesting new items (EAN-8/13, UPC-A/E, GTIN-14, check digits, number-system risk classification).
3. A **user-friendly item view** — list all PLUs, **filter by department and other fields** — pulling from the **live system** (not a standalone CSV/XML, which is how the other app works).
4. **Bulk delete** of PLUs.
5. Possibly more.

The user wants an **analysis first** to decide the most efficient approach (which app to
build on, what to port vs. rebuild) — *do not pre-commit to an implementation before that.*

## The two apps

| | **This app** (target) | **Other app** (`../verifone-commander-import-export`) |
|---|---|---|
| Stack | WinUI 3, **.NET 8** | WinForms, **.NET Framework 4.8** |
| Data source | **Live** Commander Sapphire HTTP/NAXML API | **100% file-based** (XML/CSV on disk); no live API |
| Has | PLU get/search/edit/**single delete**, departments/tax/age lookups, caching, MVVM | bulk import (column-mapping UX), rich barcode logic, validation, schema catalogs, preview/collision detection |
| Lacks | bulk import, bulk delete, rich barcode-on-ingest, live filterable item grid | any live-API access, any delete, a global item grid/filter |
| Company | Exact Technology Partners | Exact Technology Partners (already) |

**Central fact for the analysis:** the apps are on different UI stacks, so WinForms UI
can't be ported directly to WinUI — that's a rewrite. But the other app's **pure logic is
cleanly separable** and stack-agnostic. And the live-API + modern stack the user wants for
the "live item view" already exist *here*. This strongly suggests **build on this app and
harvest the other app's logic** — but confirm in the analysis (the "vice versa" option is
adding live-API to the WinForms app, which fights the user's stated goal).

## Recon of the other app (read-only Explore, 2026-05-26)

Cleanly reusable (no file/UI coupling — port as logic):
- **Barcode**: `src/UpcUtilities.cs` — `ClassifyUpc`, `Normalize` (ZeroPadOnly vs SmartCheckDigit; deliberately avoids recomputing check digits on 12–14 digit values = "H2 corruption" guard), `IsValidUpcA`, `IsValidGtin14`, `Gtin14CheckDigit`. **The highest-value, easiest-to-extract piece.**
- **Validation**: `src/Validators.cs` — per-field format rules.
- **Schema/catalogs**: `src/Catalogs.cs` — metadata-driven `ITargetCatalog`/`TargetPath` for PLU/Department/TaxRate/etc. (field paths, aliases, FK refs, formats). Useful for import column-mapping.
- **CSV parsing**: RFC 4180 reader in `src/PluConverter.cs`.

Coupled to file-based data / WinForms (rebuild, don't port):
- Column-mapping UI (`ColumnMappingForm.cs`), preview/collision UI (`PreviewForm.cs`), reference browser (`ReferenceBrowserForm.cs`), UPC mitigation dialog (`UpcMitigationForm.cs`), working-directory workflow (`WorkingDirectory.cs`), FK resolution loading from files (`ReferenceLibrary.cs`).
- **No delete** anywhere — it's additive/upsert only. Bulk delete is net-new (extend this app's existing `SapphireClient.DeletePriceLookUpAsync`).

Full per-feature coupling table is in the session history; the summary: *barcode, validation,
schema, CSV parsing = easy/high-reuse; preview UI + FK resolution = file-coupled.*

## Analysis questions for the next session (resolve before coding)

1. **Build-on target:** confirm building on THIS app (live-API, .NET 8, WinUI). Reverse only if there's a strong reason.
2. **Logic sharing mechanism:** copy/port the pure-logic files into `Core`, OR extract a shared library both apps reference? (Note the .NET 8 vs .NET Framework 4.8 gap — a shared `netstandard2.0` lib could serve both; `UpcUtilities`/`Validators` are likely netstandard-clean.)
3. **Item view + filter:** build a WinUI grid over the existing cached PLU list (`CachingSapphireClient`), filter by department/description/flags. Net-new UI.
4. **Bulk import source:** what input does the user want — CSV upload? Manual paste? Reuse the catalog/column-mapping concept in WinUI?
5. **Bulk delete UX:** multi-select in the item grid → confirm → loop `DeletePriceLookUpAsync`. Confirm safety/confirmation requirements (deletes hit the live POS).
6. **Department/FK resolution:** replace the other app's file-based `ReferenceLibrary` with live lookups (`GetDepartmentsAsync`, etc.) already in `SapphireClient`.

## Likely implementation stages (tentative — pending analysis)

1. Port `UpcUtilities` (+ tests) into `Core` (or a shared lib); wire enhanced barcode validation into the Edit/import paths.
2. Live filterable item grid in `DesktopApp` (over cached PLUs).
3. Bulk delete (multi-select grid → confirm → live deletes).
4. Bulk import (CSV → mapped → validated → live `uPLUs`).
