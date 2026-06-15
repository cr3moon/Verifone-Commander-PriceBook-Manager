# Discovered Endpoints

**Date:** 2026-06-15  
**Scope:** Verifone Commander Sapphire NAXML API — Confirmed Implemented Endpoints  
**Status:** Complete codebase scan (all cmd= references identified)

---

## Overview

This document catalogs **all endpoints confirmed to exist and be actively used** in the Verifone Commander Price Book Manager codebase. Each endpoint has been located in source code with implementation details, call sites, and response handling.

---

## Summary Table

| Endpoint | Type | Purpose | Confidence | Call Sites | Status |
|----------|------|---------|------------|-----------|--------|
| `cmd=validate` | AUTH | Session authentication | **High** | SapphireCredentialProvider.cs:71 | ✅ Implemented |
| `cmd=vPLUs` | QUERY | Fetch all PLUs | **High** | SapphireClient.cs:48 | ✅ Implemented |
| `cmd=uPLUs` | MUTATE | Create/update/delete PLUs | **High** | SapphireClient.cs:86, 112 | ✅ Implemented |
| `cmd=vposcfg` | QUERY | Fetch departments | **High** | SapphireClient.cs:121 | ✅ Implemented |
| `cmd=vpaymentcfg` | QUERY | Fetch tax rates | **High** | SapphireClient.cs:149 | ✅ Implemented |
| `cmd=vrefinteg` | QUERY | Fetch reference data (datasets) | **High** | SapphireClient.cs:176 | ✅ Implemented |

---

## Endpoint Details

### 1. `cmd=validate` (Authentication)

**Confidence:** ⭐⭐⭐⭐⭐ **HIGH**

**Location:** `src/Core/SapphireCredentialProvider.cs:71`

**Type:** Authentication / Session Establishment

**Request Format:**
```
POST /cgi-bin/NAXML
cmd=validate&user={username}&passwd={password}
```

**Response Format:**
```xml
<sapphire>
  <cookie>SESSIONCOOKIE_HEX</cookie>
</sapphire>
```

**Usage Pattern:**
- Called once per session during login (lazy-loaded on first API request).
- Cookie cached in memory; used in all subsequent requests as `&cookie={value}`.
- On cookie expiration, provider re-authenticates automatically.

**Implementation Details:**
- File: `src/Core/SapphireCredentialProvider.cs`
- Method: `GetCredentialsAsync(CancellationToken cancellationToken)`
- Returns: `ISapphireCredentials` with `NaxmlRequestUri` and `Cookie`.
- Error handling: Throws `SapphireRequestException` on invalid credentials.

**Test Coverage:**
- No explicit unit tests; integration tested via all other endpoints (all require valid cookie).

---

### 2. `cmd=vPLUs` (Fetch Price Look-Ups)

**Confidence:** ⭐⭐⭐⭐⭐ **HIGH**

**Location:** `src/Core/SapphireClient.cs:48`

**Type:** Query / Read (View-only)

**Request Format:**
```
POST /cgi-bin/NAXML
cmd=vPLUs&cookie={session_cookie}

<domain:PLUSelect xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01">
  <pageSize>1000000</pageSize>
  <page>1</page>
</domain:PLUSelect>
```

**Response Format:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<domain:PLUs xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01" page="1" ofPages="1">
  <domain:PLU>
    <upc>5901234123457</upc>
    <upcModifier>000</upcModifier>
    <description>Product Name</description>
    <department>1</department>
    <fees>
      <fee>0</fee>
    </fees>
    <pcode>0</pcode>
    <price>9.99</price>
    <flags>
      <domain:flag sysid="1" />
      <domain:flag sysid="5" />
    </flags>
    <taxRates>
      <domain:taxRate sysid="1" />
    </taxRates>
    <idChecks>
      <domain:idCheck sysid="0" />
    </idChecks>
    <SellUnit>1.00</SellUnit>
    <taxableRebate>
      <amount>0.00</amount>
    </taxableRebate>
    <maxQtyPerTrans>0.00</maxQtyPerTrans>
  </domain:PLU>
  <!-- more PLU elements -->
</domain:PLUs>
```

**Usage Pattern:**
- Called during initial cache refresh (after login).
- Currently requests all PLUs in one request (pageSize=1,000,000).
- Results cached in memory; no TTL; manual refresh via UI.

**Implementation Details:**
- File: `src/Core/SapphireClient.cs`
- Method: `GetPriceLookUpsAsync(CancellationToken cancellationToken)`
- Returns: `List<Plu>` (deserialized from XML).
- Parsing: `ModelConverter.ConvertXmlToPlu(XElement)` for each `<domain:PLU>`.
- Error handling: Individual PLU parse failures logged and skipped; operation continues.

**Test Coverage:**
- Unit tests in `src/Core.Tests/ModelConversionTests.cs:ConvertXmlToPlu_*`.
- Tests 3 scenarios: all properties missing, some missing, none missing.
- Validates default values and collection initialization.

---

### 3. `cmd=uPLUs` (Create/Update/Delete PLUs)

**Confidence:** ⭐⭐⭐⭐⭐ **HIGH**

**Location:** `src/Core/SapphireClient.cs:86, 112`

**Type:** Mutate (Write)

**Request Format (Create/Update):**
```
POST /cgi-bin/NAXML
cmd=uPLUs&cookie={session_cookie}

<domain:PLUs xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01" page="1" ofPages="1">
  <domain:PLU>
    <upc>5901234123457</upc>
    <upcModifier>000</upcModifier>
    <description>New Description</description>
    <department>2</department>
    <fees><fee>0</fee></fees>
    <pcode>0</pcode>
    <price>12.99</price>
    <flags>
      <domain:flag sysid="1" />
      <domain:flag sysid="5" />
    </flags>
    <taxRates>
      <domain:taxRate sysid="1" />
    </taxRates>
    <idChecks>
      <domain:idCheck sysid="0" />
    </idChecks>
    <SellUnit>1.00</SellUnit>
    <taxableRebate><amount>0.00</amount></taxableRebate>
    <maxQtyPerTrans>0.00</maxQtyPerTrans>
  </domain:PLU>
</domain:PLUs>
```

**Request Format (Delete):**
```
POST /cgi-bin/NAXML
cmd=uPLUs&cookie={session_cookie}

<domain:PLUs xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01" page="1" ofPages="1">
  <deletePLU>
    <upc source="keyboard">5901234123457</upc>
    <upcModifier>000</upcModifier>
  </deletePLU>
</domain:PLUs>
```

**Response Format:**
- Success: Same structure or simple confirmation (POS firmware dependent).
- Failure: XML with `VFI:Fault` / `faultCode` / `faultString`.

**Usage Pattern:**
- Update: Called when user saves PLU edits (one PLU per request currently).
- Delete: Called when user deletes a PLU (one PLU per request currently).
- Both can be batched in a single request (multiple `<domain:PLU>` or `<deletePLU>` elements).

**Implementation Details:**
- File: `src/Core/SapphireClient.cs`
- Methods:
  - `UpdatePriceLookUpAsync(Plu plu, CancellationToken cancellationToken)` — Create/update.
  - `DeletePriceLookUpAsync(long ean13, int modifier, CancellationToken cancellationToken)` — Delete.
- XML building: `ModelConverter.ConvertPluToXml(Plu)` for update; manual `<deletePLU>` for delete.
- Error handling: Throws `SapphireRequestException` on failure; cache invalidated on success.

**Test Coverage:**
- Unit tests in `src/Core.Tests/ModelConversionTests.cs:ConvertPluToXml_*`.
- Tests PLU-to-XML serialization with various field combinations.
- No integration tests for actual POS mutations.

---

### 4. `cmd=vposcfg` (Fetch Departments)

**Confidence:** ⭐⭐⭐⭐⭐ **HIGH**

**Location:** `src/Core/SapphireClient.cs:121`

**Type:** Query / Read (View-only)

**Request Format:**
```
POST /cgi-bin/NAXML
cmd=vposcfg&cookie={session_cookie}
```

**Response Format:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<posConfig>
  <department sysid="1" name="Beverages" isAllowFS="0">
    <prodCode sysid="1" />
    <taxes>
      <tax sysid="1" />
      <tax sysid="2" />
    </taxes>
    <ageValidns>
      <ageValidn sysid="0" />
    </ageValidns>
  </department>
  <department sysid="2" name="Grocery" isAllowFS="1">
    <!-- ... -->
  </department>
</posConfig>
```

**Usage Pattern:**
- Called during initial cache refresh (after login).
- Results cached in memory; refreshed on explicit "Refresh" action.
- Used for department dropdown in PLU edit UI.

**Implementation Details:**
- File: `src/Core/SapphireClient.cs`
- Method: `GetDepartmentsAsync(CancellationToken cancellationToken)`
- Returns: `List<Department>` (deserialized from XML).
- Parsing: `ModelConverter.ConvertXmlToDepartment(XElement)` for each `<department>`.
- Error handling: Individual department parse failures logged and skipped.

**Test Coverage:**
- Unit tests in `src/Core.Tests/ModelConversionTests.cs:ConvertXmlToDepartment_*`.
- Tests scenarios with all properties missing / none missing.

---

### 5. `cmd=vpaymentcfg` (Fetch Tax Rates)

**Confidence:** ⭐⭐⭐⭐⭐ **HIGH**

**Location:** `src/Core/SapphireClient.cs:149`

**Type:** Query / Read (View-only)

**Request Format:**
```
POST /cgi-bin/NAXML
cmd=vpaymentcfg&cookie={session_cookie}
```

**Response Format:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<paymentCfg>
  <taxRate sysid="1" name="Sales Tax 8.5%">
    <taxProperties rate="8.5" />
  </taxRate>
  <taxRate sysid="2" name="Sales Tax 6.0%">
    <taxProperties rate="6.0" />
  </taxRate>
</paymentCfg>
```

**Usage Pattern:**
- Called during initial cache refresh (after login).
- Results cached in memory; refreshed on explicit "Refresh" action.
- Used for tax rate checkboxes in PLU edit UI.

**Implementation Details:**
- File: `src/Core/SapphireClient.cs`
- Method: `GetTaxRatesAsync(CancellationToken cancellationToken)`
- Returns: `List<TaxRate>` (deserialized from XML).
- Parsing: `ModelConverter.ConvertXmlToTaxRate(XElement)` for each `<taxRate>`.
- Error handling: Individual tax rate parse failures logged and skipped.

**Test Coverage:**
- No dedicated unit tests for TaxRate conversion in test files reviewed.

---

### 6. `cmd=vrefinteg` (Fetch Reference/Integration Tables)

**Confidence:** ⭐⭐⭐⭐⭐ **HIGH**

**Location:** `src/Core/SapphireClient.cs:176`

**Type:** Query / Read (View-only)

**Current Usage:**
```
POST /cgi-bin/NAXML
cmd=vrefinteg&dataset=ageValidations&cookie={session_cookie}
```

**Response Format (ageValidations):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<refInteg>
  <ageValidation sysid="0" name="None" />
  <ageValidation sysid="18" name="Age 18+" />
  <ageValidation sysid="21" name="Age 21+" />
</refInteg>
```

**Usage Pattern:**
- Called with `dataset=ageValidations` parameter during initial cache refresh.
- Results cached in memory; refreshed on explicit "Refresh" action.
- Used for age validation checkboxes in PLU edit UI.
- **Extensible:** The `dataset=` parameter suggests other datasets are available (see POSSIBLE_ENDPOINTS.md).

**Implementation Details:**
- File: `src/Core/SapphireClient.cs`
- Method: `GetAgeValidationsAsync(CancellationToken cancellationToken)`
- Returns: `List<AgeValidation>` (deserialized from XML).
- Parsing: `ModelConverter.ConvertXmlToAgeValidation(XElement)` for each `<ageValidation>`.
- Error handling: Individual age validation parse failures logged and skipped.

**Known Datasets:**
- ✅ `dataset=ageValidations` — Currently implemented.
- ❓ Other datasets — Likely available but not yet discovered/implemented (see POSSIBLE_ENDPOINTS.md).

**Test Coverage:**
- No dedicated unit tests for AgeValidation conversion in test files reviewed.

---

## Namespace Information

All PLU-related XML elements use the Sapphire domain namespace:

```xml
xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01"
```

Alternative namespaces observed in test files:
- `xmlns:vs="urn:vfi-sapphire:vs.2001-10-01"` — (purpose unclear; present in test data)
- `xmlns:base="urn:vfi-sapphire:base.2001-10-01"` — (purpose unclear; present in test data)

---

## Request/Response Statistics

| Metric | Value | Note |
|--------|-------|------|
| **Protocols** | HTTPS only | No HTTP (plain-text) support observed. |
| **Port** | 443 (implied) | All requests construct `https://hostname/cgi-bin/NAXML`. |
| **HTTP Method** | POST only | All endpoints use POST (no GET, PUT, DELETE). |
| **Request Format** | Query string + optional XML body | `cmd=...&cookie=...` header + NAXML body. |
| **Response Format** | XML (UTF-8) | Single XML document per response. |
| **Pagination** | Supported (vPLUs only) | `<pageSize>` and `<page>` in request; `page` and `ofPages` in response. |
| **Authentication** | Cookie-based | Session cookie obtained via `cmd=validate`; reused in all subsequent requests. |
| **Timeout** | Unknown | No timeout handling observed in codebase; inherited from HttpClient default. |
| **Max Response Size** | Unlimited (issue) | No size cap enforced; potential DoS vector (Finding #3 in security review). |

---

## Codebase References

### Source Files
- `src/Core/SapphireClient.cs` — Main implementation.
- `src/Core/SapphireCredentialProvider.cs` — Authentication.
- `src/Core/Models/ModelConverter.cs` — XML deserialization.
- `src/Core/SapphireHttpUtil.cs` — HTTP utilities and fault detection.

### Test Files
- `src/Core.Tests/ModelConversionTests.cs` — XML serialization/deserialization tests.

### Integration Points
- `src/DesktopApp/ViewModels/CachingSapphireClient.cs` — Caching wrapper.
- `src/DesktopApp/ViewModels/SearchPageVm.cs` — Search/list UI.
- `src/DesktopApp/ViewModels/EditPageVm.cs` — Edit UI.
- `src/DesktopApp/ViewModels/AccountPageVm.cs` — Login UI.

---

## Known Limitations

1. **Single request fetch:** `cmd=vPLUs` fetches all PLUs in one request (pageSize=1,000,000). No pagination UI. Risk: memory exhaustion on large inventories.
2. **No delta sync:** All caches are full refreshes; no incremental updates.
3. **No real-time:** Cache is static until user clicks "Refresh" or logs in again.
4. **One-at-a-time mutations:** `cmd=uPLUs` for update/delete is called per PLU (no batching by app; batching possible in single request but not utilized).
5. **No selective dataset queries:** `cmd=vrefinteg` only supports `dataset=ageValidations`; no code to query other datasets (if available).

---

## Document Control

| Version | Date | Status | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-06-15 | Complete | Initial discovery (6 endpoints confirmed). |
