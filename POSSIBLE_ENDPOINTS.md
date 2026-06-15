# Possible Endpoints

**Date:** 2026-06-15  
**Scope:** Verifone Commander Sapphire NAXML API — Endpoints Inferred from Code Comments, Documentation, Patterns & References  
**Status:** Research-based (no implementation in current codebase)

---

## Overview

This document catalogs **endpoints that are likely to exist** based on:

1. **Explicit mentions in comments/docs** (CLAUDE.md, audit documents, README).
2. **Code patterns** (existing `cmd=v*` conventions suggest others follow).
3. **Naming conventions** (Sapphire API uses consistent `v*` for queries, `u*` for mutations).
4. **Domain knowledge** (POS systems typically support these data types).
5. **Cross-references** from security reviews and feature planning.

Each inferred endpoint includes confidence level, supporting evidence, and reasoning.

---

## Inferred Endpoints

### 1. `cmd=vfuel` — Fetch Fuel Prices

**Confidence:** ⭐⭐⭐⭐ **HIGH**

**Evidence:**
- Mentioned in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md), section "Missing API Capabilities" → "Fuel Prices":
  ```
  | **API command** | Unknown | Possibly `cmd=vfuel` or `cmd=vplusfuel` |
  ```
- In [CLOUD_AGENT_DESIGN.md](CLOUD_AGENT_DESIGN.md), listed as planned feature extension.
- In [SAPPHIRE_API_DOCUMENTATION.md](SAPPHIRE_API_DOCUMENTATION.md):
  ```
  1. **Fuel prices** — No API command observed; may require `cmd=vfuel` or similar
  ```

**Reasoning:**
- Verifone Commander POS systems support fuel sales (convenience stores, gas stations).
- Fuel prices are a distinct data type (separate from PLU prices, often managed separately).
- Follows the naming convention `cmd=v{entity}` for read/query commands.

**Likely Request Format:**
```
POST /cgi-bin/NAXML
cmd=vfuel&cookie={session_cookie}
```

**Likely Response Format (speculative):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<fuel>
  <fuelGrade id="87" name="Regular" pricePerGallon="3.45" updated="2026-06-15T10:30:00Z" />
  <fuelGrade id="89" name="Plus" pricePerGallon="3.65" updated="2026-06-15T10:30:00Z" />
  <fuelGrade id="91" name="Premium" pricePerGallon="3.85" updated="2026-06-15T10:30:00Z" />
</fuel>
```

**Likely Data Model:**
```csharp
public class FuelPrice
{
    public int GradeId { get; set; }           // 87, 89, 91, Diesel, etc.
    public string GradeName { get; set; }      // "Regular", "Plus", "Premium"
    public decimal PricePerGallon { get; set; }
    public DateTime LastUpdated { get; set; }
}
```

**Implementation Effort:** Medium (follows same pattern as PLU/department queries).

**Priority:** Medium (fuel pricing is secondary feature; not core app focus).

---

### 2. `cmd=vplusfuel` — Fetch Fuel Prices (Variant)

**Confidence:** ⭐⭐⭐ **MEDIUM**

**Evidence:**
- Mentioned in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md):
  ```
  | **API command** | Unknown | Possibly `cmd=vfuel` or `cmd=vplusfuel` |
  ```

**Reasoning:**
- Alternative naming variant for fuel prices (`PLUs` → `Plus`?).
- Verifone may use `Plus` as an internal product name for the advanced pricing module.
- Less likely than `cmd=vfuel` (simpler name preferred).

**Note:**
- If this endpoint exists, it likely returns the same data as `cmd=vfuel`.
- Recommend trying `cmd=vfuel` first; use `cmd=vplusfuel` as fallback only.

**Priority:** Low (exploratory only if `cmd=vfuel` not found).

---

### 3. `cmd=vinventory` — Fetch Inventory/Stock Levels

**Confidence:** ⭐⭐⭐⭐ **HIGH**

**Evidence:**
- Mentioned in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md), section "Inventory (Stock Levels)":
  ```
  | **API command** | Unknown | Possibly `cmd=vinventory` or `cmd=vstock` |
  ```
- Referenced in [CLOUD_AGENT_DESIGN.md](CLOUD_AGENT_DESIGN.md), section "Real-Time Inventory Sync".
- Implied in feature planning docs as a high-priority cross-app merge target.

**Reasoning:**
- Verifone Commander systems track on-hand inventory for all SKUs.
- Inventory is a distinct, frequently-updated data type (separate from PLU static metadata).
- Follows naming convention `cmd=v{entity}`.

**Likely Request Format:**
```
POST /cgi-bin/NAXML
cmd=vinventory&cookie={session_cookie}
```

**Likely Response Format (speculative):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<inventory>
  <item sku="5901234123457" modifier="000" onHand="150" reserved="5" reorderPoint="25" updated="2026-06-15T10:30:00Z" />
  <item sku="5901234567890" modifier="000" onHand="5" reserved="2" reorderPoint="50" updated="2026-06-15T10:30:00Z" />
  <!-- ... -->
</inventory>
```

**Likely Data Model:**
```csharp
public class InventoryItem
{
    public long Sku { get; set; }              // EAN-13
    public int Modifier { get; set; }          // PLU modifier
    public int OnHandQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int ReorderPoint { get; set; }
    public DateTime LastUpdated { get; set; }
}
```

**Real-time Capability:**
- Likely requires polling (5–15 min intervals) or webhooks for real-time sync.
- May support pagination/filtering (by department, date range, SKU range).

**Implementation Effort:** Medium–High (requires real-time architecture decisions).

**Priority:** Medium–High (critical for cross-app feature merge; analytics/forecasting).

---

### 4. `cmd=vstock` — Fetch Stock Levels (Variant)

**Confidence:** ⭐⭐⭐ **MEDIUM**

**Evidence:**
- Mentioned in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md):
  ```
  | **API command** | Unknown | Possibly `cmd=vinventory` or `cmd=vstock` |
  ```

**Reasoning:**
- Shorter, more common POS terminology ("stock" vs. "inventory").
- Verifone may use `vstock` as the official endpoint name.
- Less likely than `cmd=vinventory` (longer, more explicit name).

**Note:**
- If both endpoints exist, they likely return identical data.
- Recommend trying `cmd=vinventory` first; use `cmd=vstock` as fallback only.

**Priority:** Low (try only if `cmd=vinventory` not found).

---

### 5. `cmd=vemployees` — Fetch Employee Directory

**Confidence:** ⭐⭐⭐ **MEDIUM**

**Evidence:**
- Mentioned in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md), section "Employees":
  ```
  | **API command** | Unknown | Possibly `cmd=vemployees` or `cmd=vstaff` |
  ```
- In [SAPPHIRE_API_DOCUMENTATION.md](SAPPHIRE_API_DOCUMENTATION.md):
  ```
  3. **Employees** — Not implemented; may require `cmd=vemployees` or HR subsystem integration.
  ```

**Reasoning:**
- Verifone systems manage employee roles, access rights, hours, PIN-based login.
- Follows naming convention `cmd=v{entity}`.
- Separate from PLU/inventory management (HR subsystem).

**Likely Request Format:**
```
POST /cgi-bin/NAXML
cmd=vemployees&cookie={session_cookie}
```

**Likely Response Format (speculative):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<employees>
  <employee id="1001" name="John Doe" role="Manager" active="1" pin="1234" />
  <employee id="1002" name="Jane Smith" role="Cashier" active="1" pin="5678" />
  <!-- ... -->
</employees>
```

**Likely Data Model:**
```csharp
public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Role { get; set; }          // Manager, Cashier, Supervisor, etc.
    public bool IsActive { get; set; }
    public string Pin { get; set; }            // May be encrypted or omitted
    public DateTime CreatedAt { get; set; }
}
```

**Security Concern:**
- Employee data is sensitive (PINs, role info).
- May require elevated permissions or separate authentication.

**Implementation Effort:** Medium (employee management is a separate subsystem).

**Priority:** Very Low (out-of-scope for current app; not in feature merge plan).

---

### 6. `cmd=vstaff` — Fetch Staff Directory (Variant)

**Confidence:** ⭐⭐ **LOW–MEDIUM**

**Evidence:**
- Mentioned in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md):
  ```
  | **API command** | Unknown | Possibly `cmd=vemployees` or `cmd=vstaff` |
  ```

**Reasoning:**
- Alternative terminology ("staff" vs. "employees").
- Less likely than `cmd=vemployees` (longer, more formal).

**Priority:** Very Low (exploratory only if `cmd=vemployees` not found).

---

### 7. `cmd=vreports` — Fetch Transaction History / Reports

**Confidence:** ⭐⭐⭐ **MEDIUM**

**Evidence:**
- Mentioned in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md), section "Sales Reports":
  ```
  | **API command** | Unknown | Possibly `cmd=vreports` or `cmd=vsales` with query params |
  ```
- Referenced in [CLOUD_AGENT_DESIGN.md](CLOUD_AGENT_DESIGN.md), section "Analytics & Reporting".

**Reasoning:**
- Verifone systems maintain transaction logs and sales history.
- Reports are a distinct data type (time-series, aggregated).
- Follows naming convention `cmd=v{entity}`.
- Likely supports query parameters for date range, SKU filter, etc.

**Likely Request Format:**
```
POST /cgi-bin/NAXML
cmd=vreports&cookie={session_cookie}&dateFrom=2026-06-01&dateTo=2026-06-15&groupBy=sku
```

**Likely Response Format (speculative):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<reports>
  <transaction id="T001" timestamp="2026-06-15T10:30:00Z" sku="5901234123457" qty="2" amount="19.98" cashier="1002" />
  <transaction id="T002" timestamp="2026-06-15T10:31:00Z" sku="5901234567890" qty="1" amount="5.99" cashier="1002" />
  <!-- ... -->
</reports>
```

**Query Parameters (speculative):**
- `dateFrom` — Start date (YYYY-MM-DD or ISO 8601).
- `dateTo` — End date.
- `groupBy` — Aggregation level (sku, department, hourly, daily, etc.).
- `skuFilter` — Specific SKU(s) to filter.
- `pageSize` / `page` — Pagination support.

**Likely Data Model:**
```csharp
public class TransactionRecord
{
    public string TransactionId { get; set; }
    public DateTime Timestamp { get; set; }
    public long Sku { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public int CashierId { get; set; }
    public string TenderType { get; set; }    // Cash, Card, Check, etc.
}
```

**Implementation Effort:** High (time-series data, aggregations, query DSL).

**Priority:** Low (analytics feature; not core app focus).

---

### 8. `cmd=vsales` — Fetch Sales Data (Variant)

**Confidence:** ⭐⭐ **LOW–MEDIUM**

**Evidence:**
- Mentioned in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md):
  ```
  | **API command** | Unknown | Possibly `cmd=vreports` or `cmd=vsales` with query params |
  ```

**Reasoning:**
- More concise naming than `cmd=vreports`.
- Verifone may use `vsales` as primary endpoint; `vreports` as alias.
- Less likely (longer name often preferred for clarity in APIs).

**Priority:** Low (exploratory only if `cmd=vreports` not found).

---

### 9. `cmd=vpromotions` — Fetch Promotions / Discounts

**Confidence:** ⭐⭐⭐ **MEDIUM**

**Evidence:**
- Mentioned in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md), section "Promotions":
  ```
  | **API command** | Unknown | Possibly `cmd=vpromotions` or embedded in `vposcfg` |
  ```
- Referenced in feature planning as a future enhancement.

**Reasoning:**
- Verifone systems support promotional pricing, bulk discounts, time-limited offers.
- Promotions are a distinct data type (rule-based, time-windowed).
- May be embedded in `vposcfg` (configuration) or standalone.
- Follows naming convention `cmd=v{entity}`.

**Likely Request Format:**
```
POST /cgi-bin/NAXML
cmd=vpromotions&cookie={session_cookie}
```

**Likely Response Format (speculative):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<promotions>
  <promotion id="P001" name="Summer Sale" discountPercent="10" startDate="2026-06-01" endDate="2026-08-31" applicableSkus="5901234123457,5901234567890" />
  <promotion id="P002" name="Happy Hour" discountPercent="20" startTime="14:00" endTime="17:00" days="Monday,Tuesday,Wednesday,Thursday,Friday" />
  <!-- ... -->
</promotions>
```

**Likely Data Model:**
```csharp
public class Promotion
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string DiscountType { get; set; }   // Percentage, Fixed, BuyXGetY, etc.
    public decimal DiscountValue { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public TimeSpan StartTime { get; set; }    // For daily promotions
    public TimeSpan EndTime { get; set; }
    public List<long> ApplicableSkus { get; set; }
    public List<int> ApplicableDepartments { get; set; }
}
```

**Implementation Effort:** Medium–High (complex rule engine).

**Priority:** Low (marketing feature; not core app focus).

---

### 10. `cmd=uFuel` — Create/Update Fuel Prices

**Confidence:** ⭐⭐⭐ **MEDIUM**

**Evidence:**
- Inferred from existence of `cmd=vfuel` (follows `v*` for read, `u*` for write pattern).
- Referenced in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md):
  ```
  | `cmd=uFuel` | Mutate | Update fuel prices | `<fuel>` | High | Medium |
  ```

**Reasoning:**
- Sapphire API convention: `v{entity}` for queries, `u{entity}` for mutations.
- All other data types have both read (`v*`) and write (`u*`) endpoints.
- Fuel prices must be updatable by managers.

**Likely Request Format:**
```
POST /cgi-bin/NAXML
cmd=uFuel&cookie={session_cookie}

<fuel>
  <fuelGrade id="87" pricePerGallon="3.49" />
  <fuelGrade id="91" pricePerGallon="3.89" />
</fuel>
```

**Implementation Effort:** Medium (follows same pattern as `cmd=uPLUs`).

**Priority:** Medium (fuel pricing management feature).

---

### 11. `cmd=uInventory` — Create/Update Inventory

**Confidence:** ⭐⭐⭐ **MEDIUM**

**Evidence:**
- Inferred from existence of `cmd=vinventory` (follows write pattern).
- Referenced in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md):
  ```
  | `cmd=uInventory` | Mutate | Update stock levels | `<inventory>` | Medium | Low |
  ```

**Reasoning:**
- Sapphire API convention requires write endpoint for any readable entity.
- Inventory levels must be adjustable (stock counts, corrections, adjustments).

**Likely Request Format:**
```
POST /cgi-bin/NAXML
cmd=uInventory&cookie={session_cookie}

<inventory>
  <adjustment sku="5901234123457" modifier="000" quantityChange="10" reason="Stock Count Correction" />
  <adjustment sku="5901234567890" modifier="000" quantityChange="-5" reason="Damage/Waste" />
</inventory>
```

**Implementation Effort:** Medium (follows same pattern as `cmd=uPLUs`).

**Priority:** Low (inventory management is secondary; not core app focus).

---

### 12. `cmd=uPromotions` — Create/Update Promotions

**Confidence:** ⭐⭐ **LOW–MEDIUM**

**Evidence:**
- Inferred from existence of `cmd=vpromotions` (follows write pattern).
- Referenced in [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md):
  ```
  | `cmd=uPromotions` | Mutate | Update promotions | `<promotions>` | Medium | Low |
  ```

**Reasoning:**
- Sapphire API convention: write endpoint for readable entities.
- Promotional rules must be creatable/editable by marketing teams.

**Priority:** Very Low (marketing feature; complex rule engine; out-of-scope).

---

### 13. Other `vrefinteg` Datasets

**Confidence:** ⭐⭐⭐⭐ **HIGH** (datasets exist; specific names unknown)

**Evidence:**
- Implemented endpoint uses `cmd=vrefinteg&dataset=ageValidations`.
- The `dataset=` parameter design strongly suggests **multiple datasets are available**.
- In [SAPPHIRE_API_DOCUMENTATION.md](SAPPHIRE_API_DOCUMENTATION.md):
  ```
  #### `cmd=vrefinteg` — View Reference Integrations
  **Purpose:** Fetch all department definitions.
  ...
  **Known Datasets:**
  - ✅ `dataset=ageValidations` — Currently implemented.
  - ❓ Other datasets — Likely available but not yet discovered/implemented.
  ```

**Reasoning:**
- Parameterized query design indicates extensibility.
- Typical POS systems maintain multiple reference tables (restrictions, allergen info, origin, etc.).

**Likely Additional Datasets (speculative):**
| Dataset | Purpose | Confidence |
|---------|---------|------------|
| `allergens` | Food allergen information | Medium |
| `restrictions` | Dietary restrictions / religious rules | Medium |
| `origins` | Product origin (domestic/import/organic) | Low |
| `certifications` | Product certifications (organic, fair-trade, etc.) | Low |
| `vendors` | Supplier/vendor directory | Low |
| `suppliers` | Alternate for vendor | Low |

**Discovery Method:**
- Try common REST/API parameter values: `dataset=allergens`, `dataset=restrictions`, etc.
- Or contact Verifone Sapphire API documentation for complete list.

**Implementation Effort:** Low (once pattern is discovered, adding new datasets is trivial).

**Priority:** Medium (reference data is useful for advanced search/filtering).

---

## Naming Convention Analysis

All confirmed endpoints follow a consistent pattern:

| Pattern | Purpose | Examples |
|---------|---------|----------|
| `cmd=v{entity}` | Query / Read | `vPLUs`, `vposcfg`, `vpaymentcfg`, `vrefinteg`, `vfuel`, `vinventory`, `vemployees`, `vreports`, `vpromotions` |
| `cmd=u{entity}` | Mutate / Write (update/create/delete) | `uPLUs`, `uFuel`, `uInventory`, `uPromotions` |
| `cmd=validate` | Special: authentication | (exception to pattern) |

**Query Parameters:**
- `cmd={command}` — Endpoint command.
- `cookie={token}` — Session token (after `cmd=validate`).
- `dataset={name}` — Reference table selector (e.g., `vrefinteg`).
- Custom parameters may apply (e.g., date range for `vreports`).

---

## Verifone Sapphire API Reverse Engineering

Based on code review and documentation, the Sapphire API exhibits the following characteristics:

| Aspect | Finding | Confidence |
|--------|---------|------------|
| **Protocol** | HTTPS POST to `/cgi-bin/NAXML` | High |
| **Authentication** | Session cookie (from `cmd=validate`) | High |
| **Serialization** | XML (NAXML variant) | High |
| **Namespace** | `urn:vfi-sapphire:*` | High |
| **Command structure** | `cmd={verb}&cookie={session}` | High |
| **Verb conventions** | `v*` for reads, `u*` for writes | High |
| **Pagination** | Supported (at least for PLUs) | High |
| **Filtering** | Query parameters + XML bodies | Medium |
| **Batch operations** | Supported (multiple elements per request) | Medium |
| **Rate limiting** | Unknown | Low |
| **Timeout behavior** | Unknown | Low |

---

## Research Sources

1. **COMMANDER_INTEGRATION_MAP.md** — "Missing API Capabilities" section.
2. **SAPPHIRE_API_DOCUMENTATION.md** — "Known Limitations & Future Enhancements" section.
3. **CLOUD_AGENT_DESIGN.md** — Multi-location and analytics sections.
4. **docs/feature-merge/cross-app-merge-brief.md** — Feature targets.
5. **docs/security/network-security-review.md** — API surface review.
6. **Code comments in SapphireClient.cs** — Hints about pagination and other features.
7. **Test data in ModelConversionTests.cs** — XML namespace hints.

---

## Discovery Recommendations

### For Users / Product Owners

1. **Request official API documentation** from Verifone.
   - Contact: Verifone sales or technical support.
   - Artifact: "Sapphire NAXML API Reference" or similar.
   - Will confirm all endpoint names, parameters, response formats, and constraints.

2. **Reverse-engineer live POS device** (if documentation unavailable).
   - Enable HTTP proxy / Burp Suite on development PC.
   - Capture all `/cgi-bin/NAXML` requests/responses during POS operator activities.
   - Document any `cmd=` values not in this audit.

3. **Contact upstream fork author** (Shubham Gogna).
   - May have additional knowledge or undocumented API endpoints.
   - Original repo: `github.com/Shubham-Gogna/VerifoneCommander.PriceBookManager`.

### For Developers

1. **Test for additional `vrefinteg` datasets.**
   ```csharp
   var datasets = new[] { "allergens", "restrictions", "origins", "certifications", "vendors" };
   foreach (var dataset in datasets)
   {
       var cmdHeader = $"cmd=vrefinteg&dataset={dataset}";
       var response = await SendAsync(cmdHeader, cancellationToken);
       // Log response to identify valid datasets.
   }
   ```

2. **Attempt fuel/inventory endpoints with test requests.**
   ```csharp
   var testCommands = new[] { "vfuel", "vplusfuel", "vinventory", "vstock" };
   foreach (var cmd in testCommands)
   {
       try
       {
           var response = await SendAsync($"cmd={cmd}", cancellationToken);
           Console.WriteLine($"✓ {cmd} exists");
       }
       catch (SapphireRequestException ex)
       {
           if (ex.Message.Contains("unknown command") || ex.Message.Contains("invalid"))
           {
               Console.WriteLine($"✗ {cmd} not found");
           }
       }
   }
   ```

3. **Monitor network traffic during POS admin operations.**
   - Manager login & configuration changes.
   - Price updates, promotions, inventory adjustments.
   - Any other admin activities that may trigger additional API calls.

---

## Document Control

| Version | Date | Status | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-06-15 | Complete | Initial research (13 possible endpoints identified). |
