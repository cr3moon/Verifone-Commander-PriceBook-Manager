# Commander Integration Map

**Date:** 2026-06-15  
**Scope:** Verifone Commander Price Book Manager — Codebase Integration Architecture  
**Version:** 1.0  
**Status:** Audit (read-only analysis)

---

## Executive Summary

This document maps the **codebase implementation** to the **Sapphire API surface**, identifies how each component integrates with the Commander, tracks data flow from network to UI, and catalogs **missing API capabilities** with implications for future feature work.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     WinUI 3 Desktop App                          │
│  (DesktopApp.csproj / MainWindow + Pages)                        │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ▼
      ┌────────────────────────────────────┐
      │  Caching Layer                     │
      │  CachingSapphireClient.cs          │
      │  - In-memory cache (no TTL)        │
      │  - Refresh → full reload           │
      └────────────────────┬───────────────┘
                           │
                           ▼
      ┌────────────────────────────────────┐
      │  Core.Tests (Unit Tests)           │
      │  - Ean13HelperTests.cs             │
      │  - ModelConversionTests.cs         │
      └────────────────────────────────────┘
                           │
                           ▼
      ┌────────────────────────────────────────────────────────┐
      │  Sapphire Client Layer (Core.csproj)                   │
      │  ┌─────────────────────────────────────────────────┐  │
      │  │ ISapphireClient (interface)                     │  │
      │  │ - GetPriceLookUpsAsync()                       │  │
      │  │ - UpdatePriceLookUpAsync()                     │  │
      │  │ - DeletePriceLookUpAsync()                     │  │
      │  │ - GetDepartmentsAsync()                        │  │
      │  │ - GetTaxRatesAsync()                           │  │
      │  │ - GetAgeValidationsAsync()                     │  │
      │  └──────────────────┬────────────────────────────┘  │
      │                     │                                │
      │  ┌──────────────────▼────────────────────────────┐  │
      │  │ SapphireClient (implementation)               │  │
      │  │ - Builds NAXML request bodies                 │  │
      │  │ - Sends via HttpRequestSender                 │  │
      │  │ - Parses XML responses via ModelConverter     │  │
      │  └──────────────────┬────────────────────────────┘  │
      │                     │                                │
      └─────────────────────┼────────────────────────────────┘
                            │
       ┌────────────────────┴──────────────────┐
       │                                       │
       ▼                                       ▼
   ┌──────────────────────┐        ┌──────────────────────┐
   │ Credential Provider  │        │ HTTP Request Sender  │
   │                      │        │                      │
   │ - Login (`validate`) │        │ - HttpClient wrapper │
   │ - Cookie caching     │        │ - Custom TLS handler │
   │ - Cred validation    │        │ - Self-signed cert   │
   │                      │        │   bypass toggle      │
   └────────────┬─────────┘        └────────────┬─────────┘
                │                               │
                └──────────────┬────────────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │   HTTPS POST        │
                    │ /cgi-bin/NAXML      │
                    │                     │
                    │ Verifone Commander  │
                    │ (Live POS)          │
                    │                     │
                    │ - cmd=validate      │
                    │ - cmd=vPLUs         │
                    │ - cmd=uPLUs         │
                    │ - cmd=vposcfg       │
                    │ - cmd=vpaymentcfg   │
                    │ - cmd=vrefinteg     │
                    └─────────────────────┘
```

---

## Code-to-API Mapping

### 1. Authentication & Session Management

| **Component** | **File** | **Responsibility** | **API Integration** |
|---|---|---|---|
| `ISapphireCredentialsProvider` | `src/Core/ISapphireCredentialsProvider.cs` | Interface for credential supply | Defines contract for `GetCredentialsAsync()` |
| `SapphireCredentialProvider` | `src/Core/SapphireCredentialProvider.cs` | Implements login, caches cookie | Executes `cmd=validate&user=...&passwd=...` |
| `SapphireCredential` (inner class) | `src/Core/SapphireCredentialProvider.cs:155-165` | Holds session cookie and request URI | Opaque credential object returned to client |
| `AccountPageVm` | `src/DesktopApp/ViewModels/AccountPageVm.cs` | UI for login, error display | Calls `SetLoginCredentials()`, handles exceptions |

**Flow:**
1. User enters hostname, username, password in `AccountPageVm`.
2. `SetLoginCredentials()` stores values in provider; resets cached cookie.
3. On first API request, `SapphireClient` calls `GetCredentialsAsync()`.
4. Provider sends `cmd=validate&user=...&passwd=...` to `/cgi-bin/NAXML`.
5. Response parsed for `<cookie>` element; cached in memory.
6. All subsequent requests include `&cookie={cached_value}`.
7. On cookie expiration, provider re-authenticates automatically.

---

### 2. PLU Operations

| **Component** | **File** | **Responsibility** | **API Integration** |
|---|---|---|---|
| `Plu` (model) | `src/Core/Models/Plu.cs` | In-memory PLU representation | Maps to `<domain:PLU>` XML element |
| `SapphireClient.GetPriceLookUpsAsync()` | `src/Core/SapphireClient.cs:36-65` | Fetch all PLUs | Sends `cmd=vPLUs` with `<domain:PLUSelect>` body |
| `ModelConverter.ConvertXmlToPlu()` | `src/Core/Models/ModelConverter.cs:14-60` | Parse `<domain:PLU>` to model | Extracts fields, handles missing/malformed data |
| `SapphireClient.UpdatePriceLookUpAsync()` | `src/Core/SapphireClient.cs:68-87` | Create/update a PLU | Sends `cmd=uPLUs` with `<domain:PLU>` XML |
| `ModelConverter.ConvertPluToXml()` | `src/Core/Models/ModelConverter.cs:62-106` | Convert model to `<domain:PLU>` | Builds XML with proper formatting |
| `SapphireClient.DeletePriceLookUpAsync()` | `src/Core/SapphireClient.cs:90-117` | Delete a PLU by EAN-13 + modifier | Sends `cmd=uPLUs` with `<deletePLU>` element |
| `EditPageVm` | `src/DesktopApp/ViewModels/EditPageVm.cs` | UI for PLU edit/create | Calls `UpdatePriceLookUpAsync()`, `DeletePriceLookUpAsync()` |
| `SearchPageVm` | `src/DesktopApp/ViewModels/SearchPageVm.cs` | UI for PLU search/list | Calls `GetPriceLookUpsAsync()`, filters in-memory |

**Data Flow (Fetch):**
```
User clicks "Search"
  ↓
SearchPageVm.SearchAsync()
  ↓
CachingSapphireClient.GetPriceLookUpsAsync()
  ↓
SapphireClient.GetPriceLookUpsAsync()
  ↓
Sends: POST cmd=vPLUs&cookie=ABC123
       <domain:PLUSelect>...</domain:PLUSelect>
  ↓
Receives: <domain:PLUs>
            <domain:PLU>...</domain:PLU>
            ...
          </domain:PLUs>
  ↓
XDocument.Parse(responseContent)
  ↓
ModelConverter.ConvertXmlToPlu(element) for each <domain:PLU>
  ↓
Returns: List<Plu>
  ↓
CachingSapphireClient caches in memory
  ↓
SearchPageVm displays results
```

**Data Flow (Update):**
```
User edits PLU fields in UI
  ↓
EditPageVm.SaveAsync()
  ↓
CachingSapphireClient.UpdatePriceLookUpAsync(plu)
  ↓
SapphireClient.UpdatePriceLookUpAsync(plu)
  ↓
ModelConverter.ConvertPluToXml(plu)
  ↓
Sends: POST cmd=uPLUs&cookie=ABC123
       <domain:PLUs ...>
         <domain:PLU>...</domain:PLU>
       </domain:PLUs>
  ↓
Receives: Success or VFI:Fault
  ↓
Cache refreshed on next search
```

---

### 3. Department Operations

| **Component** | **File** | **Responsibility** | **API Integration** |
|---|---|---|---|
| `Department` (model) | `src/Core/Models/Department.cs` | In-memory department representation | Maps to `<department>` XML element |
| `SapphireClient.GetDepartmentsAsync()` | `src/Core/SapphireClient.cs:119-143` | Fetch all departments | Sends `cmd=vposcfg` (no body) |
| `ModelConverter.ConvertXmlToDepartment()` | `src/Core/Models/ModelConverter.cs:108-137` | Parse `<department>` to model | Extracts sysid, name, flags, tax refs |
| `ISapphireClientExtensions.GetDepartmentByIdAsync()` | `src/Core/ISapphireClientExtensions.cs:42-54` | Lookup department by ID | In-memory search, uses cache |
| `ISapphireClientExtensions.GetDepartmentByNameAsync()` | `src/Core/ISapphireClientExtensions.cs:56-66` | Lookup department by name | In-memory search, uses cache |
| `EditPageVm` | `src/DesktopApp/ViewModels/EditPageVm.cs` | Displays department dropdown | Populates from cached departments |

**Data Flow:**
```
SearchPageVm.SearchAsync() needs department name
  ↓
GetDepartmentByIdAsync(plu.DepartmentId)
  ↓
Uses in-memory cache (no network call)
  ↓
Returns: Department
```

---

### 4. Tax Rate Operations

| **Component** | **File** | **Responsibility** | **API Integration** |
|---|---|---|---|
| `TaxRate` (model) | `src/Core/Models/TaxRate.cs` | In-memory tax rate representation | Maps to `<taxRate>` XML element |
| `SapphireClient.GetTaxRatesAsync()` | `src/Core/SapphireClient.cs:146-169` | Fetch all tax rates | Sends `cmd=vpaymentcfg` (no body) |
| `ModelConverter.ConvertXmlToTaxRate()` | `src/Core/Models/ModelConverter.cs:139-152` | Parse `<taxRate>` to model | Extracts sysid, name, rate (percentage) |
| `ISapphireClientExtensions.GetTaxRateByIdAsync()` | `src/Core/ISapphireClientExtensions.cs:68-80` | Lookup tax rate by ID | In-memory search |
| `ISapphireClientExtensions.GetTaxRateByNameAsync()` | `src/Core/ISapphireClientExtensions.cs:82-92` | Lookup tax rate by name | In-memory search |
| `EditPageVm` | `src/DesktopApp/ViewModels/EditPageVm.cs` | Displays tax rate checkboxes | Populates from cached tax rates |

---

### 5. Age Validation Operations

| **Component** | **File** | **Responsibility** | **API Integration** |
|---|---|---|---|
| `AgeValidation` (model) | `src/Core/Models/AgeValidation.cs` | In-memory age rule representation | Maps to `<ageValidation>` XML element |
| `SapphireClient.GetAgeValidationsAsync()` | `src/Core/SapphireClient.cs:172-194` | Fetch all age validations | Sends `cmd=vrefinteg&dataset=ageValidations` |
| `ModelConverter.ConvertXmlToAgeValidation()` | `src/Core/Models/ModelConverter.cs:154-166` | Parse `<ageValidation>` to model | Extracts sysid, name |
| `ISapphireClientExtensions.GetAgeValidationByIdAsync()` | `src/Core/ISapphireClientExtensions.cs:94-106` | Lookup age rule by ID | In-memory search |
| `ISapphireClientExtensions.GetAgeValidationByNameAsync()` | `src/Core/ISapphireClientExtensions.cs:108-118` | Lookup age rule by name | In-memory search |
| `EditPageVm` | `src/DesktopApp/ViewModels/EditPageVm.cs` | Displays age validation checkboxes | Populates from cached age validations |

---

### 6. HTTP Transport & Error Handling

| **Component** | **File** | **Responsibility** | **Details** |
|---|---|---|---|
| `IHttpRequestSender` | `src/Core/IHttpRequestSender.cs` | Interface for HTTP sending | Defines `SendAsync(request, cancellationToken)` |
| `HttpClientHttpRequestSender` | `src/Core/HttpClientHttpRequestSender.cs` | Wraps `System.Net.Http.HttpClient` | Custom TLS validation callback (toggleable) |
| `SapphireHttpUtil.CreateRequest()` | `src/Core/SapphireHttpUtil.cs:22-30` | Builds POST request | Content-Type: text/plain |
| `SapphireHttpUtil.ReadResponseContentAsync()` | `src/Core/SapphireHttpUtil.cs:13-24` | Reads full response body | No streaming, no size limit (Finding #3) |
| `SapphireHttpUtil.IsUnsuccessfulResponse()` | `src/Core/SapphireHttpUtil.cs:32-51` | Validates response | Case-sensitive substring matching (Finding #4) |
| `SapphireRequestException` | `src/Core/SapphireRequestException.cs` | Custom exception type | Includes response content (Finding #7) |

**Error Flow:**
```
HTTP error or "VFI:Fault" in response
  ↓
IsUnsuccessfulResponse() returns true
  ↓
Throws SapphireRequestException(responseContent)
  ↓
Caught by UI layer (e.g., AccountPageVm)
  ↓
Logged + displayed as `LoginError` binding
```

---

### 7. Caching Layer

| **Component** | **File** | **Responsibility** | **Details** |
|---|---|---|---|
| `CachingSapphireClient` | `src/DesktopApp/ViewModels/CachingSapphireClient.cs` | In-memory cache wrapper | Wraps `ISapphireClient`, caches all results |
| Cache refresh trigger | `SearchPageVm`, `AccountPageVm` | User actions | Click "Refresh" or login → full cache reload |
| Cache key | In-memory `List<T>` | No expiration, no TTL | Lives until app close or explicit refresh |
| Cache invalidation | Manual | No automatic invalidation | User must click "Refresh" after external changes |

---

## Data Flow Diagrams

### Startup & Login

```
App Start
  ↓
App.xaml.cs initializes dependencies
  ↓
MainNavigationVm created
  ├─ AccountPageVm initialized
  ├─ SearchPageVm initialized
  ├─ EditPageVm initialized
  ├─ SettingsPageVm initialized
  └─ BulkOperationsPageVm initialized
  ↓
CachingSapphireClient created (wraps SapphireClient)
  ↓
User navigates to Account page (default)
  ↓
User enters hostname, username, password
  ↓
User clicks "Login"
  ↓
SapphireCredentialProvider.SetLoginCredentials()
  ├─ Stores hostname, username, password
  └─ Resets cached cookie
  ↓
AccountPageVm calls RefreshAsync()
  ├─ Fetches departments (cmd=vposcfg)
  ├─ Fetches tax rates (cmd=vpaymentcfg)
  ├─ Fetches age validations (cmd=vrefinteg)
  ├─ Fetches PLUs (cmd=vPLUs)
  └─ Caches all results
  ↓
All caches populated
  ↓
Success message displayed
  ↓
User can now navigate to Search/Edit pages
```

### Search Flow

```
User navigates to Search page
  ↓
SearchPageVm loaded
  ↓
User types search text (EAN, modifier, description, department)
  ↓
User clicks "Search"
  ↓
SearchPageVm.SearchAsync()
  ├─ Gets all PLUs from in-memory cache
  ├─ For each PLU:
  │  └─ Gets department from in-memory cache
  ├─ Filters PLUs matching search text
  ├─ Sorts by (EAN, modifier)
  └─ Displays results in grid
  ↓
(No network requests during search)
```

### Edit & Save Flow

```
User clicks edit button on PLU in grid
  ↓
EditPageVm loaded with PLU data
  ├─ Populates description, price, etc.
  ├─ Populates department dropdown from cache
  ├─ Populates tax rate checkboxes from cache
  └─ Populates age validation checkboxes from cache
  ↓
User modifies fields
  ↓
User clicks "Save"
  ↓
EditPageVm.SaveAsync()
  ├─ Validates PLU object
  └─ Calls SapphireClient.UpdatePriceLookUpAsync(plu)
      ├─ Builds <domain:PLU> XML
      ├─ Sends POST cmd=uPLUs&cookie=...
      └─ Receives success or error
  ↓
On success:
  ├─ Cache invalidated (PLU list reloaded)
  ├─ User returned to Search page
  └─ Success message shown
  ↓
On error:
  ├─ Error details logged
  ├─ Error message displayed to user
  └─ User remains on Edit page
```

### Delete Flow

```
User clicks delete button on PLU
  ↓
EditPageVm shows confirmation dialog
  ↓
User confirms delete
  ↓
EditPageVm.DeleteAsync()
  ├─ Calls SapphireClient.DeletePriceLookUpAsync(ean13, modifier)
  │  ├─ Builds <deletePLU> XML
  │  ├─ Sends POST cmd=uPLUs&cookie=...
  │  └─ Receives success or error
  └─ Cache invalidated on success
  ↓
On success:
  ├─ User returned to Search page
  └─ PLU no longer in results
```

---

## Missing API Capabilities

### 1. Fuel Prices

| Aspect | Status | Notes |
|--------|--------|-------|
| **Implemented** | ❌ No | No endpoint command identified |
| **API command** | Unknown | Possibly `cmd=vfuel` or `cmd=vplusfuel` |
| **Data entity** | Unknown | Likely similar to PLU (EAN + price, may include gallons/liters) |
| **Operations** | Unknown | Fetch, update/create, delete (likely) |
| **Implementation effort** | Medium | Depends on POS vendor API spec; likely similar to PLU code |
| **Priority** | Low-Medium | Feature request; not core to current app |

**Recommendation:** Request Verifone Sapphire API documentation for fuel-price endpoints from the POS vendor. Reverse-engineer from live POS if documentation unavailable.

### 2. Inventory (Stock Levels)

| Aspect | Status | Notes |
|--------|--------|-------|
| **Implemented** | ❌ No | No endpoint command identified |
| **API command** | Unknown | Possibly `cmd=vinventory` or `cmd=vstock` |
| **Data entity** | Unknown | Likely includes (EAN, on-hand, min, max, reorder point) |
| **Operations** | Unknown | Possibly view-only (no live edit) |
| **Real-time requirement** | Yes | Stock is frequently updated; caching not feasible |
| **Implementation effort** | Medium-High | Real-time data requires streaming or frequent polling |
| **Priority** | Low-Medium | Analytics/reporting feature, not core to PLU management |

**Recommendation:** Confirm if POS exposes live inventory data via Sapphire API. If so, design a real-time query with TTL-based cache (not persistent in-memory cache). Consider WebSocket or polling strategy.

### 3. Employees

| Aspect | Status | Notes |
|--------|--------|-------|
| **Implemented** | ❌ No | No endpoint command identified |
| **API command** | Unknown | Possibly `cmd=vemployees` or `cmd=vstaff` |
| **Data entity** | Unknown | Likely includes (ID, name, PIN, role, department, hours) |
| **Operations** | Unknown | View, possibly create/update/delete |
| **Scope** | Out-of-scope | Employee management is separate from PLU management |
| **Implementation effort** | Medium | Likely new data model + CRUD UI |
| **Priority** | Very Low | Not requested; out-of-scope for current app |

**Recommendation:** File as a separate feature request. Scope with product owner. Likely requires a new WinUI page similar to Search/Edit.

### 4. Sales Reports

| Aspect | Status | Notes |
|--------|--------|-------|
| **Implemented** | ❌ No | No endpoint command identified |
| **API command** | Unknown | Possibly `cmd=vreports` or `cmd=vsales` with query params |
| **Data entity** | Unknown | Likely includes (timestamp, EAN, qty, amount, cashier, tender) |
| **Historical data** | Yes | Requires querying past transactions |
| **Real-time requirement** | No | Reports are typically pulled on-demand |
| **Scope** | Out-of-scope | Business intelligence, separate from PLU management |
| **Implementation effort** | High | Requires date-range queries, aggregations, possibly export (CSV/PDF) |
| **Priority** | Very Low | Not requested; out-of-scope for current app |

**Recommendation:** Separate feature request / new app. Requires data warehouse / BI platform integration, not in-scope for this price-book manager.

### 5. Promotions

| Aspect | Status | Notes |
|--------|--------|-------|
| **Implemented** | ❌ No | Not part of PLU core data |
| **API command** | Unknown | Possibly `cmd=vpromotions` or embedded in `vposcfg` |
| **Data entity** | Unknown | Likely includes (promo ID, name, rule, discount %, date range, PLUs) |
| **Operations** | Unknown | View, create/update/delete |
| **Relationship to PLU** | Many-to-many | Promotions reference PLUs; PLUs may reference promotions |
| **Implementation effort** | Medium-High | New data model, relationship management, UI for promo rules |
| **Priority** | Low | Marketing/sales feature; not core to current app |

**Recommendation:** Scope separately. Confirm POS API supports promotions. If yes, design data model for promo rules (discount type, qualification criteria, date range, PLU list). May require a new page.

### 6. Bulk Delete (Partial Implementation)

| Aspect | Status | Notes |
|--------|--------|-------|
| **Current** | ✅ Partial | Single PLU delete is implemented |
| **Implemented** | ❌ No bulk | Each delete is a separate `cmd=uPLUs` request |
| **Optimization** | Possible | Can send multiple `<deletePLU>` elements in one request |
| **API support** | Likely yes | Verifone typically allows batch operations in one request |
| **Implementation effort** | Low | Loop pending deletions, add to single XML body, send once |
| **Priority** | Low-Medium | Performance optimization; not critical |

**Recommendation:** Batch deletion into a single `cmd=uPLUs` request with multiple `<deletePLU>` elements. Test with live POS to confirm response structure. Reduces network round-trips.

### 7. Bulk Import (Partial Implementation)

| Aspect | Status | Notes |
|--------|--------|-------|
| **Current** | ❌ Not implemented | No import UI exists |
| **Sibling app** | ✅ Partial | `verifone-commander-import-export` has CSV column-mapping, validation, barcode logic |
| **Source** | Planned | CSV upload or paste |
| **Barcode handling** | Missing | EAN-8/13, UPC-A/E, GTIN-14, check digits |
| **Validation** | Partial | Basic field rules exist; advanced logic in sibling app |
| **Collision detection** | Missing | No preview of conflicting PLUs |
| **API support** | Yes | Use batch `cmd=uPLUs` with multiple `<domain:PLU>` elements |
| **Implementation effort** | Medium-High | Port barcode + validation logic; build import UI; batch API calls |
| **Priority** | Medium | Planned feature-merge work (see cross-app-merge-brief.md) |

**Recommendation:** Part of planned feature merge. Harvest `UpcUtilities.cs` and `Validators.cs` from sibling app; port to Core (netstandard2.0 for compatibility). Build WinUI import dialog with column-mapping, preview, and conflict resolution. Batch PLUs into `cmd=uPLUs` bodies.

### 8. Live Filterable Item Grid (Partial Implementation)

| Aspect | Status | Notes |
|--------|--------|-------|
| **Current** | ✅ Partial | Search page filters in-memory cache by (EAN, modifier, description, department) |
| **Enhancement** | Missing | Add filter UI for (price range, flags, tax rates, age restrictions) |
| **Real-time** | Yes | Uses cached PLUs; requires manual refresh |
| **Sort options** | Missing | Currently sorts by (EAN, modifier) only; add (price, department, description) |
| **Pagination** | Missing | Fetch 1M+ items at once; no pagination UI |
| **Export** | Missing | No CSV/Excel export of filtered results |
| **Implementation effort** | Low-Medium | Extend SearchPageVm with filter controls; add sort; bind grid |
| **Priority** | Low-Medium | User experience improvement; not critical |

**Recommendation:** Add filter UI for price range, department (multi-select), flags, tax rates. Add sort column headers. Test with large inventories (10k+ PLUs) for performance. Consider adding CSV export for bulk operations (e.g., pricing review).

---

## API Endpoint Catalog

### Implemented Endpoints

| Command | Verb | Purpose | Request Body | Response | Implemented |
|---------|------|---------|---|---|---|
| `cmd=validate` | Auth | Obtain session cookie | `user=...&passwd=...` | `<cookie>` | ✅ Yes |
| `cmd=vPLUs` | Query | Fetch all PLUs | `<domain:PLUSelect>` | `<domain:PLUs>` | ✅ Yes |
| `cmd=uPLUs` | Mutate | Create/update/delete PLU(s) | `<domain:PLUs>` or `<deletePLU>` | Success or fault | ✅ Yes |
| `cmd=vposcfg` | Query | Fetch departments | (none) | `<posConfig><department>` | ✅ Yes |
| `cmd=vpaymentcfg` | Query | Fetch tax rates | (none) | `<paymentCfg><taxRate>` | ✅ Yes |
| `cmd=vrefinteg&dataset=ageValidations` | Query | Fetch age validations | (none) | `<refInteg><ageValidation>` | ✅ Yes |

### Likely Endpoints (Not Confirmed)

| Command | Verb | Purpose | Expected Response | Likelihood | Priority |
|---------|------|---------|---|---|---|
| `cmd=vfuel` | Query | Fetch fuel prices | `<fuel>` | High | Medium |
| `cmd=vinventory` or `cmd=vstock` | Query | Fetch stock levels | `<inventory>` | High | Low |
| `cmd=vemployees` or `cmd=vstaff` | Query | Fetch employee list | `<employees>` | Medium | Very Low |
| `cmd=vreports` or `cmd=vsales` | Query | Fetch transaction history | `<reports>` | Medium | Very Low |
| `cmd=vpromotions` | Query | Fetch promotions | `<promotions>` | Medium | Low |
| `cmd=uFuel` | Mutate | Update fuel prices | `<fuel>` | High | Medium |
| `cmd=uInventory` | Mutate | Update stock levels | `<inventory>` | Medium | Low |
| `cmd=uPromotions` | Mutate | Update promotions | `<promotions>` | Medium | Low |

---

## Implementation Dependencies

### Build Dependencies

**Solution:** `src/VerifoneCommander.PriceBookManager.sln`

- **Core.csproj** (no external NuGet; only `System.*` and `Microsoft.Extensions.Logging`)
- **DesktopApp.csproj** (WinUI 3, CommunityToolkit.Mvvm, Windows App SDK 2.1.3)
- **Console.csproj** (Core only)
- **Core.Tests.csproj** (xUnit, Core)

### Runtime Dependencies

- **.NET 8 LTS** (bundled in self-contained MSIX)
- **Windows App SDK 2.1.3 runtime** (bundled in self-contained MSIX)
- **Verifone Commander POS** (network target; requires HTTPS, NAXML API enabled)

### Network Requirements

- **Outbound:** HTTPS POST to `https://{hostname}/cgi-bin/NAXML`
- **Port:** 443 (HTTPS)
- **Network:** Local LAN or VPN (RFC1918 / private networks typical)
- **TLS:** Server certificate validation (default); toggle to allow self-signed (user setting)

---

## Caching & Performance Characteristics

| Operation | Network Calls | Cache Used | Latency | Scalability |
|---|---|---|---|---|
| **Login** | 1 (validate) | N/A | ~100-500ms | N/A |
| **Initial refresh** | 4 (vPLUs, vposcfg, vpaymentcfg, vrefinteg) | None (first load) | ~500ms–5s | Depends on inventory size |
| **Search** | 0 | Yes (all PLUs + departments) | ~10–100ms | O(n) in-memory scan |
| **View PLU details** | 0 | Yes (cached PLU + lookups) | ~1–10ms | O(1) cache lookup |
| **Edit PLU** | 1 (uPLUs) | Cache invalidated after | ~100–500ms | N/A |
| **Delete PLU** | 1 (uPLUs) | Cache invalidated after | ~100–500ms | N/A |
| **Bulk delete N PLUs** | N (current) or 1 (optimized) | Cache invalidated after | ~100ms–5s | O(N) or O(1) batched |

---

## Security Posture Summary

### Strengths

- ✅ Credentials stored in memory only (no disk persistence).
- ✅ Username/password never reused after initial `cmd=validate`.
- ✅ Session cookie is opaque (no JWT / parseable data).
- ✅ XML update bodies are built with `XElement` (escaping correct).
- ✅ No telemetry, C2, or exfiltration channels.

### Weaknesses (Findings)

| Finding | Severity | Status | Notes |
|---------|----------|--------|-------|
| #1: TLS validation disabled | High | Closed | Now user-toggled (default off) |
| #2: Permissive redirect/proxy | High | Open | `AllowAutoRedirect`, `UseProxy` at defaults |
| #3: Unbounded response buffering | High | Open | Fetches 1M+ items; no size cap |
| #4: Fragile fault detection | Medium | Open | Case-sensitive substring matching |
| #5: Malformed field coercion | Medium | Open | Silent default on missing numerics |
| #6: Foreign key validation | Low | Open | No null-checks before dereference |
| #7: Raw response in logs/UI | Low | Open | Response content logged + bound to UI |
| #8: Console inventory dump | Info | Open | Full PLU list printed to stdout |

---

## Integration Testing Checklist

- [ ] Login with valid credentials; verify cookie obtained.
- [ ] Login with invalid credentials; verify fault response.
- [ ] Fetch PLUs on clean cache; verify count and data accuracy.
- [ ] Search with various filters; verify in-memory matching.
- [ ] Edit PLU; verify XML structure sent to POS.
- [ ] Delete PLU; verify `<deletePLU>` element structure.
- [ ] Fetch departments, tax rates, age validations; verify lookups work.
- [ ] Force cookie expiration (mock); verify re-authentication.
- [ ] Large inventory (10k+ PLUs); verify performance, no memory leak.
- [ ] Malformed response; verify error handling (not silent default).
- [ ] Network error (timeout, DNS fail); verify exception propagated.
- [ ] TLS certificate validation toggle; verify both modes work.

---

## References

- **Sapphire API Documentation:** [SAPPHIRE_API_DOCUMENTATION.md](SAPPHIRE_API_DOCUMENTATION.md)
- **Feature merge brief:** [docs/feature-merge/cross-app-merge-brief.md](docs/feature-merge/cross-app-merge-brief.md)
- **Security review:** [docs/security/network-security-review.md](docs/security/network-security-review.md)
