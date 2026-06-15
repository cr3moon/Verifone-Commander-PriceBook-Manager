# Sapphire API Documentation

**Date:** 2026-06-15  
**Scope:** Verifone Commander Price Book Manager — HTTP/NAXML Live API Integration  
**Version:** 1.0  
**Status:** Audit (read-only analysis)

---

## Overview

The Verifone Sapphire API is a **live HTTP/XML (NAXML)** interface to the Commander POS controller. The Price Book Manager communicates exclusively with this API — there is no file-based data persistence. All PLU (Price Look Up), department, tax, and age-validation data is fetched and written directly to the live POS device.

- **Protocol:** HTTPS POST over local network
- **Base Path:** `/cgi-bin/NAXML` on the configured Commander hostname
- **Authentication:** Session cookies (after `cmd=validate`)
- **Data Format:** NAXML (XML subset) with domain namespace `urn:vfi-sapphire:np.domain.2001-07-01`
- **Platform:** .NET 8, HttpClient-based, with custom certificate validation (user-toggled TLS bypass for self-signed POS certs)

---

## Authentication

### 1. Credential Capture

Credentials are stored **in memory only** during the app session; none is persisted to disk.

| Component | Storage | Lifespan |
|-----------|---------|----------|
| Hostname | Settings JSON (`settings.json`) | Session + app restarts |
| Username | Settings JSON | Session + app restarts |
| Password | Memory | Current session only |
| Cookie | Memory | Until session expires on POS or app closes |

**Files involved:**
- `src/Core/SapphireCredentialProvider.cs` — manages login flow and cookie caching
- `src/DesktopApp/ViewModels/AccountPageVm.cs` — UI for credential capture

### 2. Login Flow (Authentication)

**Command:** `cmd=validate&user={username}&passwd={password}`

**Request Structure:**
```
POST /cgi-bin/NAXML HTTP/1.1
Host: {hostname}
Content-Type: text/plain
Content-Length: {length}

cmd=validate&user=manager&passwd=MyPassword
```

**Response (Success):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<sapphire>
  <cookie>SESSIONCOOKIE_HEX_VALUE</cookie>
</sapphire>
```

**Response (Failure):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<sapphire>
  <VFI:Fault xmlns:VFI="...">
    <faultCode>...</faultCode>
    <faultString>Invalid credentials</faultString>
  </VFI:Fault>
</sapphire>
```

**Processing:**
1. Client sends unencrypted username/password to `/cgi-bin/NAXML`.
2. POS validates credentials and returns a session cookie (`<cookie>` element).
3. Cookie is cached in memory; all subsequent requests use `&cookie={cached_value}`.
4. Cookie persists for the lifetime of the session or until POS session expires.
5. On cookie expiration (no cookie element in response), the provider re-authenticates.

**Security notes:**
- Username/password are transmitted via URL-encoded query parameters (not form-encoded body).
- No channel encryption beyond HTTPS (and TLS validation is user-toggled).
- Cookie is opaque to the client; no JWT or standard session token structure.
- Re-authentication is automatic and transparent to the UI layer.

---

## NAXML Request Structure

All non-validation requests follow this pattern:

```
POST /cgi-bin/NAXML HTTP/1.1
Host: {hostname}
Content-Type: text/plain

cmd={command}&cookie={session_cookie}

[optional NAXML body]
```

### Request Components

| Part | Purpose | Example |
|------|---------|---------|
| `cmd={command}` | API endpoint verb | `vPLUs`, `uPLUs`, `vposcfg`, `vpaymentcfg`, `vrefinteg` |
| `cookie={value}` | Session token from login | `SESSIONCOOKIE_HEX_VALUE` |
| Body | XML payload (for mutations) | `<domain:PLUs ... ><domain:PLU>...</domain:PLU></domain:PLUs>` |

### Example: Fetch PLUs

```
POST /cgi-bin/NAXML HTTP/1.1
Host: 192.168.31.10
Content-Type: text/plain

cmd=vPLUs&cookie=ABC123DEF456

<domain:PLUSelect xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01">
  <pageSize>1000000</pageSize>
  <page>1</page>
</domain:PLUSelect>
```

### XML Namespace

All PLU-related elements use:
```xml
xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01"
```

- Prefix `domain:` is required for PLU (`<domain:PLU>`), flags, tax rates, and other domain objects.
- Non-domain elements (departments, age validations) do **not** use the namespace in the response but are qualified when building requests.

---

## Commander Endpoints (cmd=...)

### Currently Implemented

| Command | Purpose | Response | Parameters |
|---------|---------|----------|-----------|
| `validate` | Authentication | `<cookie>` element | `user`, `passwd` |
| `vPLUs` | Fetch all PLUs (view) | XML tree of `<domain:PLU>` elements | NAXML body with `<pageSize>`, `<page>` |
| `uPLUs` | Update/create/delete PLUs (update) | Echoed request or success confirmation | NAXML body with `<domain:PLU>` or `<deletePLU>` |
| `vposcfg` | View POS configuration (departments) | XML with `<department>` elements | None |
| `vpaymentcfg` | View payment config (tax rates) | XML with `<taxRate>` elements | None |
| `vrefinteg` | View reference/integration tables | XML with referenced entities | `dataset={name}` query param |

### Endpoint Details

#### `cmd=vPLUs` — Fetch PLUs

**Purpose:** Retrieve all price look-ups from the live POS.

**Request:**
```xml
<domain:PLUSelect xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01">
  <pageSize>1000000</pageSize>
  <page>1</page>
</domain:PLUSelect>
```

**Response:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<domain:PLUs xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01" page="1" ofPages="1">
  <domain:PLU>
    <upc>5901234123457</upc>
    <upcModifier>000</upcModifier>
    <description>Example Product</description>
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

**Notes:**
- `pageSize` is currently hardcoded to `1,000,000` (one request fetches all PLUs).
- Pagination is supported via `page` attribute but not currently used.
- **Issue (Finding #3):** Fetching 1M+ items at once in a single HTTP request can cause memory exhaustion on large inventory.

#### `cmd=uPLUs` — Create/Update/Delete PLUs

**Purpose:** Mutate PLUs on the live POS (create new, update existing, or delete by EAN-13 + modifier).

**Request (Update/Create):**
```xml
<domain:PLUs xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01" page="1" ofPages="1">
  <domain:PLU>
    <upc>5901234123457</upc>
    <upcModifier>000</upcModifier>
    <description>Updated Description</description>
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

**Request (Delete):**
```xml
<domain:PLUs xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01" page="1" ofPages="1">
  <deletePLU>
    <upc source="keyboard">5901234123457</upc>
    <upcModifier>000</upcModifier>
  </deletePLU>
</domain:PLUs>
```

**Response:** 
- Success: Same structure or simple confirmation (varies by POS firmware version).
- Failure: XML with `VFI:Fault` / `faultCode` / `faultString`.

**Notes:**
- PLU identity is keyed on **EAN-13 (`<upc>`) + 3-digit modifier (`<upcModifier>`)**.
- All elements within a PLU are required when updating.
- `<fee>` values are numeric IDs from the POS configuration.
- Delete is performed by sending a `<deletePLU>` element in the same command envelope.
- `source="keyboard"` attribute is literal in the delete request.

#### `cmd=vposcfg` — View POS Configuration (Departments)

**Purpose:** Fetch all department definitions.

**Response:**
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
    ...
  </department>
</posConfig>
```

**Fields:**
- `sysid` — Unique system ID (referenced by PLUs and tax configurations).
- `name` — Human-readable department name.
- `isAllowFS` — Boolean (0/1) indicating if the department accepts food stamps/EBT.
- `prodCode sysid` — Product code reference.
- `taxes` — List of applicable tax rates (`<tax>` with `sysid`).
- `ageValidns` — List of age-validation rules (`<ageValidn>` with `sysid`).

#### `cmd=vpaymentcfg` — View Payment Configuration (Tax Rates)

**Purpose:** Fetch all tax rate definitions.

**Response:**
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

**Fields:**
- `sysid` — Unique system ID (referenced by PLUs and departments).
- `name` — Tax rate label.
- `rate` — Numeric tax percentage (e.g., `8.5` for 8.5%).

#### `cmd=vrefinteg&dataset=ageValidations` — View Reference Integrations

**Purpose:** Fetch age-validation rule definitions (e.g., for restricted-age items like alcohol/tobacco).

**Response:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<refInteg>
  <ageValidation sysid="0" name="None" />
  <ageValidation sysid="18" name="Age 18+" />
  <ageValidation sysid="21" name="Age 21+" />
</refInteg>
```

**Fields:**
- `sysid` — Unique system ID (referenced by PLUs and departments).
- `name` — Age restriction label.

---

## Data Entities

### PLU (Price Look-Up)

**In-memory model:** `src/Core/Models/Plu.cs`

```csharp
public class Plu
{
    public long Ean13 { get; set; }              // Barcode (EAN-13, 13 digits)
    public int Modifier { get; set; }            // Modifier (000-999, 3 digits)
    public string Description { get; set; }      // Product name
    public int DepartmentId { get; set; }        // FK: Department.SystemId
    public ISet<int> FeeIds { get; set; }        // Fee IDs (typically {0})
    public int ProductCodeId { get; set; }       // Product code (usually 0)
    public double Price { get; set; }            // Sale price ($ amount, 2 decimals)
    public ISet<int> FlagIds { get; set; }       // Flag IDs ({1, 5} default)
    public ISet<int> TaxRateIds { get; set; }    // FK: TaxRate.SystemId (set)
    public ISet<int> AgeValidationIds { get; set; } // FK: AgeValidation.SystemId (set)
    public double SellUnit { get; set; }         // Units per transaction (default 1.00)
    public double TaxableRebateAmount { get; set; } // Rebate (0.00 typical)
    public double MaxQuantityPerTransaction { get; set; } // Qty limit per txn
}
```

**Key constraints:**
- **EAN-13 identity:** Uniqueness is enforced by (EAN-13, modifier) tuple.
- **Barcode format:** `upc` is stored as a 14-digit zero-padded string in XML (`5901234123457` → `05901234123457`).
- **Modifier:** 3-digit zero-padded string (`0` → `000`).

### Department

**In-memory model:** `src/Core/Models/Department.cs`

```csharp
public class Department
{
    public int SystemId { get; set; }                    // Unique ID
    public string Name { get; set; }                     // Department name
    public bool AllowFoodStamps { get; set; }           // SNAP/EBT eligible?
    public int ProductCodeId { get; set; }              // Product code ID
    public ISet<int> TaxRateIds { get; set; }          // Applicable taxes (set)
    public ISet<int> AgeValidationIds { get; set; }    // Applicable age rules (set)
}
```

### TaxRate

**In-memory model:** `src/Core/Models/TaxRate.cs`

```csharp
public class TaxRate
{
    public int SystemId { get; set; }  // Unique ID
    public string Name { get; set; }   // Tax label (e.g., "Sales Tax 8.5%")
    public double Rate { get; set; }   // Percentage (e.g., 8.5)
}
```

### AgeValidation

**In-memory model:** `src/Core/Models/AgeValidation.cs`

```csharp
public class AgeValidation
{
    public int SystemId { get; set; }  // Unique ID
    public string Name { get; set; }   // Age rule label (e.g., "Age 21+")
}
```

---

## Caching Strategy

**Location:** `src/DesktopApp/ViewModels/CachingSapphireClient.cs`

- **PLUs:** Cached after first fetch; no TTL. Manual refresh available via UI (Search page).
- **Departments:** Cached after first fetch; refreshed alongside PLUs.
- **Tax Rates:** Cached after first fetch; refreshed alongside PLUs.
- **Age Validations:** Cached after first fetch; refreshed alongside PLUs.

**Refresh:** All caches are refreshed together when the user clicks "Refresh" or performs a search. No selective/incremental cache updates.

**Cache key:** In-memory collection; no persistent cache file.

---

## Error Handling

### Response Validation

**File:** `src/Core/SapphireHttpUtil.cs:47-50`

A response is considered a **failure** if any of these conditions hold:

1. HTTP status code is not `2xx` (success).
2. Response body contains the exact-case substring `"VFI:Fault"`.
3. Response body contains the exact-case substring `"faultCode"`.
4. Response body contains the exact-case substring `"faultString"`.

Otherwise, the response is treated as success.

**Security issue (Finding #4):** Fault detection is case-sensitive and substring-based; unexpected XML or altered casing can bypass detection.

### Exception Handling

- **Network errors:** Thrown as `SapphireRequestException` with response content.
- **Parsing errors:** Individual PLU/department/tax records that fail to parse are logged and skipped; the operation continues with remaining records.
- **Missing fields:** Fields that fail to parse are silently defaulted:
  - Numerics → `0` or default constant.
  - Sets → empty or default set.
  - **Security issue (Finding #5):** Silently defaulting required fields can poison the cache.

---

## Security Considerations

### Transport Layer (TLS)

- **Default:** TLS certificate validation is **enabled** (POS must present a valid certificate).
- **User override:** User can toggle `Settings.AllowUntrustedCertificates = true` on the Settings page to accept self-signed or IP-only certificates.
- **Finding #1 (closed):** TLS bypass is now user-controlled (default off), not unconditional.
- **Finding #2 (open):** `AllowAutoRedirect` and `UseProxy` remain at permissive defaults; a MITM can redirect or proxy the session.

### Credential Transmission

- Credentials are sent via URL-encoded query parameters (`cmd=validate&user=...&passwd=...`).
- No URL-encoding is applied; a username/password containing `&`, `\r`, or `\n` can inject extra parameters (low risk, local-only).
- **Finding:** Restrict credential character sets to prevent parameter injection.

### Data Validation

- **XML parsing:** XDocument with default settings (XXE possible but unlikely; no hardened parser settings).
- **Field coercion:** Malformed numeric fields silently default to `0` (finding #5).
- **Foreign keys:** No validation that referenced department/tax/age-validation IDs exist before caching and later dereferencing (finding #6).

---

## Performance Observations

### Pagination

- Currently fetches **all** PLUs in a single request (`pageSize=1,000,000`).
- For large inventories (10k+ items), this can cause memory spikes and slow startup.
- **Issue (Finding #3):** No response-size limits; a malicious or faulty POS returning huge XML can exhaust memory.

### Caching

- All data is cached in memory after first fetch.
- Subsequent searches and edits use the cache (no live re-fetch per item).
- Refresh forces a full re-fetch of all catalogs.

### Network Round-trips

1. **Login:** 1 request (`cmd=validate`).
2. **Initial load:** 4 requests (`vPLUs`, `vposcfg`, `vpaymentcfg`, `vrefinteg`).
3. **Search:** 0 additional network requests (in-memory cache).
4. **Edit/Save:** 1 request per PLU (or batch PLUs in one `uPLUs` body).
5. **Delete:** 1 request per PLU deletion.

---

## Integration Layers

### HTTP Layer

**Files:**
- `src/Core/HttpClientHttpRequestSender.cs` — HttpClient wrapper with custom TLS validation callback.
- `src/Core/SapphireHttpUtil.cs` — Request/response utilities, fault detection, content reading.

### Credential Provider

**File:** `src/Core/SapphireCredentialProvider.cs`

- Manages login flow and cookie caching.
- Lazy-loads cookie on first API request (can throw `SapphireRequestException`).

### Client

**File:** `src/Core/SapphireClient.cs`

- Implements `ISapphireClient` interface.
- Public methods: `GetPriceLookUpsAsync`, `UpdatePriceLookUpAsync`, `DeletePriceLookUpAsync`, `GetDepartmentsAsync`, `GetTaxRatesAsync`, `GetAgeValidationsAsync`.
- All methods are async and accept `CancellationToken`.

### Caching Layer

**File:** `src/DesktopApp/ViewModels/CachingSapphireClient.cs`

- Wraps `ISapphireClient` and adds in-memory caching.
- Refreshes all caches together; no selective updates.

### UI Layer

**Files:**
- `src/DesktopApp/ViewModels/SearchPageVm.cs` — PLU search and filter.
- `src/DesktopApp/ViewModels/EditPageVm.cs` — PLU creation/edit.
- `src/DesktopApp/ViewModels/AccountPageVm.cs` — Login and credential management.

---

## Known Limitations & Future Enhancements

### Not Implemented

1. **Fuel prices** — No API command observed; may require `cmd=vfuel` or similar (requires POS vendor documentation).
2. **Inventory (stock levels)** — Not part of the current API surface; may require real-time POS query.
3. **Employees** — Not implemented; may require `cmd=vemployees` or HR subsystem integration.
4. **Sales reports** — Requires historical data queries; not part of PLU/config scope.
5. **Promotions** — Not part of the core PLU API; may require separate command.
6. **Bulk delete** — Single PLU delete is implemented via `<deletePLU>` in `uPLUs` body; multi-delete requires loop.
7. **Bulk import** — Not implemented; planned for feature merge (sibling app has logic).
8. **Advanced filtering** — Fetch all → in-memory filter only; no server-side query DSL.

### Performance Gaps

- No pagination; single massive request for all PLUs.
- No incremental cache updates; full refresh only.
- No request cancellation (app-level; HTTP cancellation token is supported).

### Security Gaps

- See **Security Considerations** section above; findings #2–7 are open.

---

## References

- **Upstream repo:** `Shubham Gogna / VerifoneCommander.PriceBookManager` (MIT, forked and rebranded by Exact Technology Partners).
- **Verifone Sapphire documentation:** Not included in this codebase; API reverse-engineered from implementation.
- **POS configuration:** Commander controller must have HTTP/NAXML API enabled; typically accessible on the local network (192.168.x.x).
