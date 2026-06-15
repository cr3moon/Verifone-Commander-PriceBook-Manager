# Cloud Agent Design — Sapphire API Extension Architecture

**Date:** 2026-06-15  
**Scope:** Verifone Commander Price Book Manager — Cloud Integration & API Gateway Architecture  
**Version:** 1.0  
**Status:** Conceptual Design (non-binding, audit-informed)

---

## Executive Summary

This document proposes **cloud-capable extension layers** for the Price Book Manager, enabling:

1. **Multi-location management** — Sync PLU/pricing/inventory across multiple POS devices.
2. **Cloud API abstraction** — Isolate app from POS API versioning; add telemetry/audit.
3. **Asynchronous operations** — Bulk import/export without blocking the desktop app.
4. **Real-time sync** — Inventory/pricing pushed from cloud to POS.
5. **Advanced analytics** — Sales trends, SKU performance, inventory forecasting.

This is **architectural guidance**, not an immediate implementation plan. The current app remains **local-first** and **self-contained**. Cloud components are **opt-in** and **decoupled**.

---

## Design Principles

### 1. Local-First, Cloud-Optional

- Desktop app operates **fully independently** of cloud (no hard dependency).
- Cloud endpoints are **proxies** to local POS devices (not authoritative data store).
- Authentication remains **per-POS** (user credentials never leave local network).
- Sync is **explicit** (user chooses what to sync to cloud, when).

### 2. Separation of Concerns

```
┌─────────────────────────────────────────────────────────────┐
│                    User's Network (on-prem)                 │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  Verifone Commander (POS) — Sapphire API            │   │
│  └──────────────────────────────┬──────────────────────┘   │
│                                 │                           │
│  ┌──────────────────────────────▼──────────────────────┐   │
│  │  Desktop App (WinUI / .NET 8)                        │   │
│  │  - Live API only (no cloud hard-dependency)         │   │
│  │  - Local caching, search, edit                      │   │
│  │  - Opt-in cloud sync endpoints                      │   │
│  └──────────────────────────────┬──────────────────────┘   │
└─────────────────────────────────┼──────────────────────────┘
                                  │
                                  ▼
                    ┌──────────────────────────┐
                    │   HTTPS to Cloud (MTI)   │
                    │ (optional, user-enabled) │
                    └──────────────────────────┘
                                  │
                                  ▼
                    ┌──────────────────────────────────┐
                    │  Cloud API Gateway                │
                    │  (middleware, auth, audit)       │
                    └──────────────┬───────────────────┘
                                   │
        ┌──────────────────────────┼──────────────────────────┐
        ▼                          ▼                          ▼
  ┌──────────────┐          ┌──────────────┐         ┌──────────────┐
  │ Multi-Site   │          │  Analytics   │         │  Inventory   │
  │ Manager      │          │  & Audit Log │         │  Forecaster  │
  └──────────────┘          └──────────────┘         └──────────────┘
        │                          │                          │
        ▼                          ▼                          ▼
  ┌──────────────────────────────────────────────────────────┐
  │               Cloud Database / Data Lake                 │
  │     (PostgreSQL / DynamoDB / Snowflake)                  │
  └──────────────────────────────────────────────────────────┘
```

### 3. Stateless API Gateway

- No session state in cloud; each request includes auth token + context.
- Cloud acts as a **proxy/aggregator**, not an authority.
- POS remains the **source of truth** for PLU/inventory data.
- Cloud caches are **read-through** and **eventually consistent**.

### 4. Encryption & Isolation

- **In transit:** TLS 1.3+ with certificate pinning (cloud API endpoint).
- **At rest:** AES-256 encryption for sensitive data (credentials, PII).
- **Tenant isolation:** Multi-tenant SaaS; data partitioned by account ID.
- **Access control:** Role-based (admin, manager, viewer); API resource-level.

---

## Architecture Layers

### Layer 1: Local Desktop App (Current)

**Scope:** In-memory cache, search, edit, single-location operations.

**Components:**
- `SapphireClient` — Communicates directly with POS.
- `CachingSapphireClient` — Local caching wrapper.
- UI (WinUI 3) — Search, edit, settings pages.

**Cloud interface:** Optional `CloudSyncClient` (new component, not required).

---

### Layer 2: Cloud API Gateway

**Purpose:** Normalize POS API versions, add audit/telemetry, enable multi-location.

**Endpoints (RESTful / GraphQL):**

```
POST /api/v1/sync/plu-snapshot
├─ Request: { accountId, locationId, timestamp }
├─ Response: Paginated list of PLUs from POS cache
└─ Purpose: Sync PLU state to cloud for analytics

POST /api/v1/sync/inventory-snapshot
├─ Request: { accountId, locationId, timestamp }
├─ Response: Current stock levels for all SKUs
└─ Purpose: Track inventory over time

POST /api/v1/bulk-operations/import
├─ Request: { accountId, locationId, file: CSV, mappings: [...] }
├─ Response: Job ID (async)
└─ Purpose: Queue bulk import; process asynchronously

GET /api/v1/analytics/sku-performance
├─ Request: { accountId, dateRange, groupBy: department|flag }
├─ Response: Sales, margin, velocity metrics
└─ Purpose: Business intelligence

POST /api/v1/settings/sync
├─ Request: { accountId, department|taxRate|ageValidation, action: create|update|delete }
├─ Response: Success or conflict
└─ Purpose: Sync configuration changes across locations
```

**Authentication:**
- Bearer token (JWT or OAuth 2.0).
- Token includes `accountId`, `locationId`, `roles`.
- Refresh token for session management.

**Rate limiting:**
- 100 req/min per account.
- Bulk operations: 10 jobs/min per location.

---

### Layer 3: Multi-Location Manager

**Purpose:** Coordinate pricing/inventory across multiple POS devices.

**Features:**

1. **Bulk pricing updates** — Push a price change to 10 POS devices in one operation.
   ```
   UI: "Update all Beverages department to +5% margin"
      ↓
   Cloud: Parse change → Calculate new prices for 50 SKUs
      ↓
   Cloud: Queue 50 async `uPLUs` jobs (batch to each POS)
      ↓
   Cloud: Report: "50 SKUs updated on 10 devices; 2 failures"
   ```

2. **Master data sync** — One location (headquarters) is the source; others pull.
   ```
   Local edit on HQ device
      ↓
   Desktop app saves to local POS
      ↓
   Cloud: Detects change (polling or webhook)
      ↓
   Cloud: Broadcasts to 9 satellite locations
      ↓
   Satellite devices: `uPLUs` auto-applied (if enabled)
   ```

3. **Merge conflicts** — Handle divergent changes across locations.
   ```
   Location A: Raises price to $9.99
   Location B: Raises price to $10.49 (same SKU, same time)
      ↓
   Cloud: Conflict detected
      ↓
   Resolution:
   - Policy-based: Higher price wins (or lower wins).
   - Manual review: Admin approves in cloud console.
   ```

---

### Layer 4: Real-Time Inventory Sync

**Purpose:** Keep inventory current across all locations without manual refresh.

**Architecture:**

```
POS A ──┐
         ├─→ Cloud Sync Agent (polling or webhook)
POS B ──┤    │
         │    ├─→ Consolidates stock levels
POS C ──┘    │
             ├─→ Forecasting ML model
             │
             └─→ Push notifications:
                 - "SKU X stock critical on 3 devices"
                 - "Recommend order: Y units by tomorrow"
```

**Implementation options:**

| Approach | Latency | Overhead | Complexity |
|----------|---------|----------|-----------|
| Polling (5min) | 5 min | Low | Low |
| POS webhook (event-driven) | <1 sec | Medium | Medium |
| MQTT broker (pub-sub) | <1 sec | High | High |

**Recommended:** Start with polling (5–15 min intervals); scale to webhooks if latency becomes critical.

---

### Layer 5: Analytics & Reporting

**Purpose:** Extract business intelligence from aggregate POS data.

**Data pipeline:**

```
POS devices (sales events)
   ↓ (daily)
Cloud ETL
   ├─ Normalize timestamp, currency, locale
   ├─ Deduplicate (POS may retry uploads)
   ├─ Aggregate by (SKU, department, date, location)
   └─ Load to data warehouse
   ↓
Materialized views
   ├─ Daily sales by SKU/location
   ├─ Margin analysis by department
   ├─ Inventory turnover
   └─ Promotional effectiveness
   ↓
Cloud dashboard & export
   ├─ Web UI (next to desktop app)
   ├─ Mobile app
   └─ Programmatic API (BI tools: Tableau, Power BI)
```

**Queries:**

```sql
-- Q1: Which SKUs have highest margin?
SELECT sku, SUM(sales) as total_sales, SUM(margin) as total_margin, 
       SUM(margin) / SUM(sales) * 100 as margin_pct
FROM sales_fact
WHERE account_id = ? AND date >= ?
GROUP BY sku
ORDER BY margin_pct DESC;

-- Q2: Inventory forecast for next 30 days
SELECT sku, current_stock, avg_daily_sales, 
       current_stock / NULLIF(avg_daily_sales, 0) as days_on_hand,
       CASE WHEN days_on_hand < 14 THEN 'REORDER' ELSE 'OK' END as status
FROM inventory_forecast
WHERE location_id = ?;
```

---

## Integration Paths

### Path 1: Opt-In Cloud Sync (Minimal Impact)

**Goal:** Add cloud endpoints without forcing migration.

**Steps:**

1. **Add cloud configuration to Settings page.**
   - Checkbox: "Enable cloud sync?"
   - Text field: Cloud API endpoint URL.
   - OAuth button: "Authorize cloud account".

2. **Implement `CloudSyncClient` (new class in Core).**
   ```csharp
   public class CloudSyncClient : ICloudSyncClient
   {
       public async Task SyncPluSnapshotAsync(List<Plu> plus, CancellationToken ct)
       {
           // POST /api/v1/sync/plu-snapshot
       }
       
       public async Task SyncInventoryAsync(List<InventoryItem> items, CancellationToken ct)
       {
           // POST /api/v1/sync/inventory-snapshot
       }
       
       public async Task<List<Plu>> FetchRemoteMultiLocationPlusAsync(string query, CancellationToken ct)
       {
           // GET /api/v1/plu/search?q=...
       }
   }
   ```

3. **Add sync button to SearchPageVm.**
   ```
   "Sync to Cloud" button
      ↓
   Uploads current cache to cloud
      ↓
   Shows: "✓ 1,234 PLUs synced (5 conflicts)"
   ```

4. **Handle conflicts on return.**
   ```
   Cloud: "Prices differ on 5 SKUs between your device and cloud master"
   User: [Resolve: Keep Local | Use Cloud Master]
   ```

**Effort:** 2–3 weeks (single developer).

**Risk:** Low. No change to existing desktop flow; cloud is optional.

---

### Path 2: Bulk Import via Cloud (Async)

**Goal:** Offload large imports to cloud worker pool.

**Flow:**

```
User: [Upload CSV] → Desktop app
   ↓
Desktop app: Parse & validate CSV locally
   ↓
Desktop app: [Preview conflicts, column mapping]
   ↓
User: [Confirm import]
   ↓
Desktop app: POST /api/v1/bulk-operations/import
   Payload: { file: CSV, mappings: [...], validations: [...] }
   ↓
Cloud: Receives job, queues to worker pool
   Returns: Job ID
   ↓
Desktop app: Shows modal "Importing... (Job #12345)"
   ↓
Cloud: Worker processes CSV
   ├─ Parse rows
   ├─ Apply barcode normalization (from sibling app UpcUtilities)
   ├─ Validate fields
   ├─ Batch `uPLUs` calls to POS (50 PLUs per request)
   ├─ Log: 1,234 created, 56 updated, 12 failed
   └─ Callback to desktop app
   ↓
Desktop app: Shows result: "✓ 1,234 PLUs imported successfully"
```

**Implementation:**

- **Desktop:** `CloudBulkImportClient.cs` — Upload CSV, poll job status.
- **Cloud:** AWS Lambda + SQS + S3 (or Azure Functions).
- **Retry logic:** Auto-retry failed POS requests (up to 3x).
- **Audit:** Log all imports (who, when, how many, result).

**Effort:** 4–6 weeks.

**Risk:** Medium. Adds cloud infrastructure dependency (but optional).

---

### Path 3: Real-Time Inventory Sync (Webhook)

**Goal:** Push current stock levels from POS to cloud every 5 minutes.

**Architecture:**

```
POS A: Inventory module (runs on POS every 5 min)
   ├─ Gathers current stock for all SKUs
   └─ POST https://cloud.example.com/api/v1/inventory-webhook
      Payload: { locationId, timestamp, items: [{ sku, onHand, reserved }] }
   
Cloud: Receives webhook
   ├─ Validates signature (HMAC-SHA256)
   ├─ Deduplicates (same payload may arrive 2x)
   ├─ Stores in inventory fact table
   ├─ Updates materialized view (current stock)
   └─ Triggers analytics update

Desktop app: Polls /api/v1/inventory/current
   ├─ Every 10 min (or on-demand)
   └─ Displays: "Beverage A: 234 units (synced 2 min ago)"
```

**Requires:**

- POS firmware support for HTTP callbacks (likely available; vendor confirms).
- Cloud endpoint + signature verification.
- Database schema for time-series stock data.

**Effort:** 3–4 weeks (infrastructure + firmware testing).

**Risk:** Medium-High (POS firmware dependency, network reliability).

---

## Data Model Extensions

### Cloud PLU Entity

```csharp
public class CloudPluSnapshot
{
    public Guid Id { get; set; }                    // UUID
    public string AccountId { get; set; }           // Tenant key
    public string LocationId { get; set; }          // POS device ID
    public DateTime CapturedAt { get; set; }        // When synced
    public long Ean13 { get; set; }                 // Barcode
    public int Modifier { get; set; }               // Modifier
    public string Description { get; set; }         // Product name
    public decimal Price { get; set; }              // Current price
    public int DepartmentId { get; set; }           // Dept reference
    public IList<int> TaxRateIds { get; set; }      // Applicable taxes
    public string SourceHash { get; set; }          // For dedup
    public DateTime CreatedAt { get; set; }         // DB timestamp
    public DateTime UpdatedAt { get; set; }         // Last modified
}
```

**Index strategy:**
```sql
CREATE INDEX idx_plu_snapshot_account_location_ean 
  ON plu_snapshot (account_id, location_id, ean13, modifier);

CREATE INDEX idx_plu_snapshot_captured_at 
  ON plu_snapshot (captured_at DESC);
```

---

### Cloud Inventory Entity

```csharp
public class InventoryFactRow
{
    public Guid Id { get; set; }
    public string AccountId { get; set; }
    public string LocationId { get; set; }
    public long Ean13 { get; set; }
    public int OnHandQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public DateTime SnapshotDate { get; set; }      // Date only (fact grain)
    public int DaysSinceLastSale { get; set; }      // Derived
    public decimal CurrentPrice { get; set; }       // FK to PLU snapshot
}
```

---

### Cloud Audit Log

```csharp
public class AuditLogEntry
{
    public Guid Id { get; set; }
    public string AccountId { get; set; }
    public string UserId { get; set; }              // From JWT
    public string LocationId { get; set; }
    public string Action { get; set; }              // "PLU_UPDATE", "IMPORT_START", etc.
    public string ResourceType { get; set; }        // "PLU", "BULK_JOB"
    public string ResourceId { get; set; }          // EAN or Job ID
    public Dictionary<string, object> Changes { get; set; } // { "price": [9.99, 10.99] }
    public string Status { get; set; }              // "SUCCESS", "FAILED"
    public string ErrorMessage { get; set; }        // If failed
    public DateTime CreatedAt { get; set; }
}
```

---

## API Specification (OpenAPI 3.0)

### POST /api/v1/sync/plu-snapshot

**Authenticate:** Bearer token (JWT).

**Request:**
```json
{
  "accountId": "acct_12345",
  "locationId": "pos_device_01",
  "timestamp": "2026-06-15T14:30:00Z",
  "plus": [
    {
      "ean13": 5901234123457,
      "modifier": 0,
      "description": "Product A",
      "price": 9.99,
      "departmentId": 1,
      "taxRateIds": [1, 2],
      "ageValidationIds": []
    },
    ...
  ]
}
```

**Response (200):**
```json
{
  "synced": 1234,
  "conflicts": 5,
  "conflictDetails": [
    {
      "ean13": 5901234567890,
      "field": "price",
      "localValue": 9.99,
      "cloudValue": 10.49,
      "resolution": "NEEDS_REVIEW"
    }
  ]
}
```

**Response (400):**
```json
{
  "error": "INVALID_REQUEST",
  "message": "Missing required field: accountId"
}
```

**Response (401):**
```json
{
  "error": "UNAUTHORIZED",
  "message": "Invalid or expired token"
}
```

---

### POST /api/v1/bulk-operations/import

**Request:**
```json
{
  "accountId": "acct_12345",
  "locationId": "pos_device_01",
  "fileName": "weekly_pricing.csv",
  "fileSize": 524288,
  "fileHash": "sha256:abc123...",
  "mappings": [
    { "csvColumn": "UPC", "pluField": "ean13" },
    { "csvColumn": "Name", "pluField": "description" },
    { "csvColumn": "Price", "pluField": "price" },
    { "csvColumn": "Dept", "pluField": "departmentId" }
  ],
  "options": {
    "conflictResolution": "SKIP",  // or "OVERWRITE"
    "validateBarcodes": true,
    "normalizeBarcode": true,
    "dryRun": false
  }
}
```

**Response (202 Accepted):**
```json
{
  "jobId": "job_987654",
  "status": "QUEUED",
  "statusUrl": "/api/v1/bulk-operations/jobs/job_987654",
  "estimatedDuration": 300  // seconds
}
```

**Poll for result:**
```
GET /api/v1/bulk-operations/jobs/job_987654

Response (200 COMPLETED):
{
  "jobId": "job_987654",
  "status": "COMPLETED",
  "startedAt": "2026-06-15T14:35:00Z",
  "completedAt": "2026-06-15T14:40:15Z",
  "result": {
    "totalRows": 1234,
    "created": 1100,
    "updated": 120,
    "skipped": 14,
    "failed": 0,
    "summary": "Import successful"
  }
}
```

---

## Deployment Architecture (AWS)

```
┌──────────────────────────────────────────────────────────┐
│                    AWS Cloud Platform                    │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  ┌─ CloudFront (CDN) ────────────────────────────────┐  │
│  │  - Caches /api/v1/analytics/* responses           │  │
│  │  - Geo-distributed edge                           │  │
│  └─────────────────────────────────────────────────┬─┘  │
│                                                    │     │
│  ┌─ API Gateway ──────────────────────────────────┤     │
│  │  - Rate limiting (100 req/min per acct)         │     │
│  │  - JWT validation                               │     │
│  │  - Request logging                              │     │
│  └─────────────────────┬───────────────────────────┘     │
│                        │                                  │
│  ┌─ Lambda (Serverless Functions) ────────────────┐     │
│  │  - POST /sync/plu-snapshot                      │     │
│  │  - POST /sync/inventory-snapshot                │     │
│  │  - GET /analytics/*                             │     │
│  │  - POST /bulk-operations/import                 │     │
│  │  (Cold start: ~1s; warm: ~100ms)                │     │
│  └─────────────┬──────────────────────────────────┘     │
│                │                                         │
│  ┌─ SQS (Message Queue) ──────────────────────────┐     │
│  │  - Enqueue bulk import jobs                     │     │
│  │  - Decouple API from processing                 │     │
│  │  - Dead-letter queue for failures               │     │
│  └─────────────┬──────────────────────────────────┘     │
│                │                                         │
│  ┌─ EC2 / ECS (Workers) ──────────────────────────┐     │
│  │  - Consume import jobs from SQS                 │     │
│  │  - Call local POS API (curl + NAXML)            │     │
│  │  - Batch PLU updates (50 per request)           │     │
│  │  - Auto-scaling: 1–10 instances (peak hours)    │     │
│  └─────────────┬──────────────────────────────────┘     │
│                │                                         │
│  ┌─ RDS PostgreSQL (Primary DB) ──────────────────┐     │
│  │  - plu_snapshot (time-series)                   │     │
│  │  - inventory_fact (daily grain)                 │     │
│  │  - audit_log (immutable)                        │     │
│  │  - accounts, locations (config)                 │     │
│  │  - Encrypted at rest (KMS)                      │     │
│  │  - Multi-AZ failover                            │     │
│  │  - Automated backups (30-day retention)         │     │
│  └─────────────┬──────────────────────────────────┘     │
│                │                                         │
│  ┌─ Redshift (Data Warehouse) ────────────────────┐     │
│  │  - Sales/inventory analytics                    │     │
│  │  - Materialized views (refresh: 1x/day)         │     │
│  │  - Multi-year historical data                   │     │
│  │  - Unload to S3 (Parquet) for BI tools          │     │
│  └─────────────┬──────────────────────────────────┘     │
│                │                                         │
│  ┌─ S3 (Object Storage) ──────────────────────────┐     │
│  │  - Audit log archives (long-term)               │     │
│  │  - Parquet export for BI                        │     │
│  │  - Temp storage for large CSV uploads           │     │
│  │  - Versioning + encryption enabled              │     │
│  └─────────────┬──────────────────────────────────┘     │
│                │                                         │
│  ┌─ CloudWatch + X-Ray ───────────────────────────┐     │
│  │  - Metrics: latency, error rates, throughput    │     │
│  │  - Logs: centralized (all Lambda, RDS)          │     │
│  │  - Alarms: SLA violations, failures              │     │
│  │  - Tracing: end-to-end request flow              │     │
│  └─────────────┬──────────────────────────────────┘     │
│                │                                         │
│  ┌─ Secrets Manager ──────────────────────────────┐     │
│  │  - Store POS credentials (rotated monthly)      │     │
│  │  - Database passwords                           │     │
│  │  - API keys for third-party services            │     │
│  └─────────────────────────────────────────────────┘     │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

**Deployment pipeline:**
```
Developer: Push to main branch
   ↓
GitHub Actions (CI/CD)
   ├─ Run unit tests (Core.Tests)
   ├─ Run integration tests (against mock POS)
   ├─ Build Docker image (for Lambda / ECS)
   ├─ SAST scan (security)
   └─ Push to ECR
   ↓
CloudFormation (IaC)
   ├─ Validate template
   ├─ Create changeset
   └─ Prompt for approval
   ↓
Manual approval (prod deployments)
   ↓
CloudFormation applies
   ├─ Updates Lambda functions
   ├─ Updates RDS schema (migrations)
   ├─ Updates security groups
   └─ Creates DNS records
   ↓
Smoke tests (canary)
   ├─ Call /api/v1/health
   ├─ POST mock PLU snapshot
   ├─ Verify audit log entry
   └─ Report: Pass / Rollback
```

---

## Security Considerations

### Authentication & Authorization

- **Desktop to cloud:** OAuth 2.0 (implicit flow) with PKCE.
  - User authenticates once (browser).
  - Token stored locally (encrypted in app data).
  - Refresh token rotated every 7 days.

- **Cloud internal:** Service-to-service: mTLS (mutual TLS) with certificate pinning.

- **Database access:** IAM roles (not passwords).
  - Lambda assumes role → connects to RDS (temporary credentials).
  - Role restricted to `SELECT`, `INSERT` on specific tables.

### Data Protection

- **In transit:** TLS 1.3+ (CloudFront → Lambda, Lambda → RDS, app → CloudFront).
- **At rest:** AES-256 encryption.
  - Database: AWS KMS managed keys.
  - S3: Bucket encryption enabled.
  - Secrets: AWS Secrets Manager (rotated).

- **Logging:** PII redacted from logs by default.
  - "Updated PLU #12345" (not UPC if sensitive).
  - Passwords never logged.

### Network Isolation

- **Private subnets:** RDS, Redshift in private VPC.
- **Bastion host:** Admin access via SSH key pair (no passwords).
- **WAF rules:** Protect API Gateway against OWASP Top 10.
  - Rate limiting by IP / API key.
  - SQL injection detection.
  - XSS filters.

### Compliance

- **SOC 2 Type II:** Annual audit (if required by enterprise customers).
- **GDPR:** Data residency (EU customers → Frankfurt region).
- **PCI DSS:** Unnecessary (no payment data stored; but credential handling reviewed).
- **Audit trail:** Immutable log of all data mutations (who, what, when, result).

---

## Cost Model

### AWS Monthly (100 accounts, 500 locations, 1M PLUs)

| Service | Monthly Cost | Notes |
|---------|---|---|
| API Gateway | ~$150 | 10M requests/mo |
| Lambda | ~$200 | 100M invocations/mo, 512MB/100ms avg |
| RDS PostgreSQL | ~$300 | db.t3.medium, Multi-AZ, 100GB storage |
| S3 | ~$50 | 500GB archival |
| CloudFront | ~$50 | 10TB transfer/mo |
| Redshift | ~$200 | Single-node cluster (dc2.large), 160GB |
| CloudWatch | ~$50 | Logs, metrics, alarms |
| **Total** | **~$1,000/mo** | ~$10 per account/mo |

**Scaling:** Double to $2,000–$3,000/mo if 1,000 accounts added (infrastructure scales automatically).

---

## Migration Path (Phased)

### Phase 1: Foundation (Weeks 1–6)

- [ ] Design cloud database schema.
- [ ] Implement API Gateway + Lambda scaffold.
- [ ] Build `CloudSyncClient` in desktop app (optional).
- [ ] Implement OAuth flow in desktop app.
- [ ] Test with mock POS.

**Deliverable:** Cloud API accepts PLU snapshots; stores to RDS.

### Phase 2: Bulk Import (Weeks 7–12)

- [ ] Implement SQS + worker pool.
- [ ] Port barcode validation from sibling app.
- [ ] Build import UI in desktop app.
- [ ] Add conflict resolution logic.
- [ ] Test with real POS (500–1,000 PLU import).

**Deliverable:** Desktop app can submit CSV; cloud processes asynchronously.

### Phase 3: Analytics (Weeks 13–18)

- [ ] Design Redshift schema.
- [ ] Build ETL pipeline (RDS → Redshift).
- [ ] Implement analytics API endpoints.
- [ ] Build web dashboard (React).
- [ ] Export to BI tools (Tableau connector).

**Deliverable:** Web dashboard with 10+ pre-built reports; BI tool integration.

### Phase 4: Real-Time Sync (Weeks 19–24)

- [ ] Implement webhook receiver (POS → cloud).
- [ ] Build inventory sync UI (desktop app).
- [ ] Implement mobile app (React Native) for alerts.
- [ ] Load test (100 POS devices, 5-min sync).
- [ ] Document API for POS firmware team.

**Deliverable:** Live inventory visible in desktop/mobile apps; alerts on stock critical.

---

## Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|------|---|---|---|
| POS firmware doesn't support webhooks | Medium | High | Start with polling (5-min intervals); vendor confirms capability early. |
| Cloud outage disconnects desktop app | Low | Medium | Desktop app fully functional offline; sync queued when cloud returns. |
| Data sync conflicts (multi-location) | Medium | Medium | Implement policy-based conflict resolution + manual review UI. |
| API rate limit abuse | Low | Medium | Implement token bucket + account-level quotas; monitor for attacks. |
| Regulatory / data residency | Medium | High | Plan for GDPR / regional databases; document data flow. |

---

## References

- **Sapphire API Documentation:** [SAPPHIRE_API_DOCUMENTATION.md](SAPPHIRE_API_DOCUMENTATION.md)
- **Commander Integration Map:** [COMMANDER_INTEGRATION_MAP.md](COMMANDER_INTEGRATION_MAP.md)
- **Feature Merge Brief:** [docs/feature-merge/cross-app-merge-brief.md](docs/feature-merge/cross-app-merge-brief.md)
- **Security Review:** [docs/security/network-security-review.md](docs/security/network-security-review.md)

---

## Appendix: Example Cloud Agent Implementation

### Pseudocode: Bulk Import Job Worker

```csharp
public class BulkImportJobWorker
{
    private readonly ISapphireClient pluosClient;
    private readonly ICloudDataStore dataStore;
    private readonly ILogger logger;

    public async Task ProcessJobAsync(BulkImportJob job, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Processing job {jobId}", job.Id);

            // Download CSV from S3
            var csvContent = await dataStore.DownloadCsvAsync(job.S3Key, ct);
            
            // Parse
            var records = CsvParser.Parse(csvContent, job.Mappings);
            logger.LogInformation("Parsed {count} records from CSV", records.Count);

            // Validate & normalize barcodes
            var validated = new List<PluRecord>();
            foreach (var record in records)
            {
                try
                {
                    var normalized = UpcUtilities.Normalize(record.Barcode);
                    if (!UpcUtilities.IsValidUpcA(normalized) && 
                        !UpcUtilities.IsValidGtin14(normalized))
                    {
                        logger.LogWarning("Invalid barcode: {barcode}", record.Barcode);
                        job.FailedCount++;
                        continue;
                    }
                    validated.Add(record with { NormalizedBarcode = normalized });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Barcode normalization failed");
                    job.FailedCount++;
                }
            }

            // Batch PLU updates (50 per request)
            var batches = validated.Batch(50).ToList();
            foreach (var batch in batches)
            {
                var pluUpdates = batch.Select(r => ConvertToPlu(r)).ToList();
                
                try
                {
                    await pluosClient.UpdatePriceLookUpsAsync(pluUpdates, ct);
                    job.CreatedCount += batch.Count;
                    
                    // Log to audit trail
                    await dataStore.LogAuditAsync(
                        accountId: job.AccountId,
                        action: "IMPORT_BATCH",
                        resourceId: job.Id,
                        changes: new { count = batch.Count, status = "SUCCESS" },
                        ct: ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Batch update failed; retrying...");
                    job.FailedCount += batch.Count;
                    
                    // Retry individual records (fallback)
                    foreach (var record in batch)
                    {
                        try
                        {
                            await pluosClient.UpdatePriceLookUpsAsync(
                                new[] { ConvertToPlu(record) }, ct);
                            job.CreatedCount++;
                            job.FailedCount--;
                        }
                        catch
                        {
                            // Log failure, move on
                        }
                    }
                }
            }

            job.Status = "COMPLETED";
            job.CompletedAt = DateTime.UtcNow;
            await dataStore.UpdateJobAsync(job, ct);
            
            logger.LogInformation(
                "Job completed: {created} created, {failed} failed",
                job.CreatedCount, job.FailedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job processing failed");
            job.Status = "FAILED";
            job.ErrorMessage = ex.Message;
            await dataStore.UpdateJobAsync(job, ct);
        }
    }
}
```

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-06-15 | Audit Team | Initial design (conceptual) |

