# Unknown Endpoint Discovery Plan

**Date:** 2026-06-15  
**Author:** Technical Reconnaissance  
**Purpose:** Systematic methodology for discovering undocumented Sapphire API endpoints on live Verifone Commander POS devices  
**Status:** Research & Implementation Planning (no code changes)

---

## Executive Summary

The Verifone Sapphire API exposes **at least 6 confirmed endpoints** (`cmd=validate`, `cmd=vPLUs`, `cmd=uPLUs`, `cmd=vposcfg`, `cmd=vpaymentcfg`, `cmd=vrefinteg`). Evidence strongly suggests **7–13 additional endpoints** exist based on naming patterns, domain knowledge, and implicit references in documentation.

This document outlines a **three-phase discovery strategy** combining:
1. **Probing** — Test inferred endpoints automatically
2. **Reverse Engineering** — Monitor live POS API calls
3. **Documentation Research** — Contact Verifone for official specs

---

## Phase 1: Automated Endpoint Probing

### 1.1 Candidate Endpoints (Sorted by Confidence)

**HIGH Confidence (⭐⭐⭐⭐):**

| Endpoint | Confidence | Evidence | Priority |
|----------|-----------|----------|----------|
| `cmd=vfuel` | ⭐⭐⭐⭐ | Explicit mention in docs; POS feature; naming pattern | 1 |
| `cmd=vinventory` | ⭐⭐⭐⭐ | Explicit mention in docs; cross-app merge target; core feature | 2 |
| Other `vrefinteg` datasets | ⭐⭐⭐⭐ | Parameterized query design; others likely exist | 3 |

**MEDIUM Confidence (⭐⭐⭐):**

| Endpoint | Confidence | Evidence | Priority |
|----------|-----------|----------|----------|
| `cmd=vstock` | ⭐⭐⭐ | Alternative name for `vinventory` | 4 |
| `cmd=vpromotions` | ⭐⭐⭐ | Marketing/promo feature; documentation mention | 5 |
| `cmd=vreports` | ⭐⭐⭐ | Analytics feature; feature planning mention | 6 |
| `cmd=vsales` | ⭐⭐⭐ | Alternative for `vreports` | 7 |
| `cmd=vemployees` | ⭐⭐⭐ | Employee management; HR feature | 8 |

**LOW Confidence (⭐⭐):**

| Endpoint | Confidence | Evidence | Priority |
|----------|-----------|----------|----------|
| `cmd=vplusfuel` | ⭐⭐ | Alternative naming variant | 9 |
| `cmd=vstaff` | ⭐⭐ | Alternative for `vemployees` | 10 |
| `cmd=vvendors` | ⭐⭐ | Supplier management (speculative) | 11 |
| `cmd=vcustomer` | ⭐⭐ | Loyalty/customer data (speculative) | 12 |

---

### 1.2 Probing Algorithm

**Objective:** For each candidate, determine if endpoint exists without causing errors.

**Method: Minimal Request Approach**

For query endpoints (`v*`), send the lightest possible request:
```
POST /cgi-bin/NAXML HTTP/1.1
cmd=vfuel&cookie={valid_session_cookie}

(empty body)
```

**Response Classification:**

| Response Pattern | Status | Inference |
|------------------|--------|-----------|
| **HTTP 200 + valid XML** | ✅ **EXISTS** | Endpoint implemented |
| **HTTP 200 + `<VFI:Fault>`** | ❓ **MAYBE** | Endpoint exists but rejects query |
| **HTTP 400 + "unknown command"** | ❌ **NOT FOUND** | Command unknown to POS |
| **HTTP 404** | ❌ **NOT FOUND** | Path not found (rare) |
| **HTTP 500** | ❓ **ERROR** | POS server error (retry) |
| **Timeout (>30s)** | ❌ **SKIP** | POS hung; move to next |

**Success Criteria:**
- Response is well-formed XML (no parse error)
- Response does not contain fault markers: `VFI:Fault`, `faultCode`, `faultString`
- HTTP status code is 2xx

**Failure Criteria:**
- Fault markers present → endpoint not supported
- Status code 4xx → endpoint not found
- Timeout → POS unresponsive (skip, don't retry)

---

### 1.3 Probing Code Sketch

```csharp
public class EndpointProber
{
    private readonly ISapphireClient client;
    
    public async Task<ProbeResults> ProbeEndpointsAsync(
        List<string> candidates,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var results = new ProbeResults();
        
        foreach (var candidate in candidates)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
                
                var sw = Stopwatch.StartNew();
                
                // Send minimal request
                var response = await client.SendAsync(
                    $"cmd={candidate}",
                    body: null,
                    linkedCts.Token);
                
                sw.Stop();
                
                // Classify response
                var status = ClassifyResponse(response);
                
                results.Add(new ProbeResult
                {
                    Command = candidate,
                    Status = status,
                    ResponseLength = response.Length,
                    DurationMs = sw.ElapsedMilliseconds,
                    ResponsePreview = response.Substring(0, Math.Min(200, response.Length))
                });
            }
            catch (OperationCanceledException)
            {
                results.Add(new ProbeResult
                {
                    Command = candidate,
                    Status = ProbeStatus.Timeout,
                    Error = "Request timed out"
                });
            }
            catch (SapphireRequestException ex)
            {
                results.Add(new ProbeResult
                {
                    Command = candidate,
                    Status = ProbeStatus.Error,
                    Error = ex.Message
                });
            }
        }
        
        return results;
    }
    
    private ProbeStatus ClassifyResponse(string response)
    {
        if (string.IsNullOrEmpty(response))
            return ProbeStatus.EmptyResponse;
        
        if (response.Contains("VFI:Fault") || 
            response.Contains("faultCode") || 
            response.Contains("faultString") ||
            response.Contains("unknown command"))
            return ProbeStatus.NotFound;
        
        // Try to parse as XML
        try
        {
            XDocument.Parse(response);
            return ProbeStatus.Found;
        }
        catch (XmlException)
        {
            return ProbeStatus.MalformedResponse;
        }
    }
}

public enum ProbeStatus
{
    Found,               // ✅ Endpoint exists and responds with valid XML
    NotFound,            // ❌ Endpoint returns "unknown command" or fault
    Error,               // ⚠️ POS error (5xx or exception)
    Timeout,             // ❌ Request exceeded timeout
    MalformedResponse,   // ⚠️ Response is not valid XML
    EmptyResponse        // ⚠️ No response body
}

public class ProbeResult
{
    public string Command { get; set; }
    public ProbeStatus Status { get; set; }
    public string Error { get; set; }
    public long ResponseLength { get; set; }
    public long DurationMs { get; set; }
    public string ResponsePreview { get; set; }
}
```

---

### 1.4 Probing Execution Plan

**Test Environment:**
- Live Verifone Commander POS device on network
- Valid user credentials
- Network connectivity (HTTPS)

**Execution:**
1. Log in to POS (acquire session cookie)
2. Iterate through HIGH confidence candidates (priority 1–3)
3. Wait 1 second between requests (POS rate limiting)
4. Log all results to CSV
5. If time permits, test MEDIUM confidence candidates
6. Summarize findings to UNKNOWN_ENDPOINTS_PROBED.md

**Expected Duration:** ~3–5 minutes (20 candidates @ 5 sec/probe avg)

**Safety Measures:**
- Use `TimeSpan` timeout to prevent hanging
- No mutations; all requests are read-only queries
- No authentication changes; no risk to POS state
- Reuse existing session cookie; no additional auth attempts

---

## Phase 2: Reverse Engineering via Network Capture

### 2.1 Network Capture Setup

**Objective:** Monitor all HTTP/NAXML traffic when POS admin performs operations.

**Tools:** (pick one)
- **Burp Suite Community** — Full proxy with request/response history
- **Fiddler** — Simple proxy + UI
- **mitmproxy** — CLI-based MITM proxy
- **Wireshark** — Packet-level capture (more complex)

**Setup Steps:**

1. **Configure Proxy on PC:**
   ```
   Windows Settings > Network > Proxy
   → Manual proxy setup: 127.0.0.1:8080
   ```

2. **Install Burp Certificate** (for HTTPS):
   ```
   Burp → Help → Install Burp CA certificate
   → Export to .pem or .cer file
   → Windows > Certificates > Trust as Root CA
   ```

3. **Disable TLS Verification (Temporary):**
   - Toggle `Settings.AllowUntrustedCertificates = true` in Price Book Manager
   - This allows interception of self-signed POS cert

4. **Start Capture:**
   - Open Burp Suite
   - Enable proxy listener on 127.0.0.1:8080
   - Start HTTP history recording

---

### 2.2 Operations to Capture

**On POS Admin Interface, Perform:**

| Operation | Expected Endpoints |
|-----------|-------------------|
| Login (authenticate) | `cmd=validate` |
| View PLU list | `cmd=vPLUs` |
| View departments | `cmd=vposcfg` |
| View tax rates | `cmd=vpaymentcfg` |
| View age validations | `cmd=vrefinteg&dataset=ageValidations` |
| **Update fuel prices** | `cmd=uFuel` or `cmd=vfuel` (discovery!) |
| **Check inventory** | `cmd=vinventory` or `cmd=vstock` (discovery!) |
| **View reports/sales** | `cmd=vreports` or `cmd=vsales` (discovery!) |
| **Manage promotions** | `cmd=vpromotions` (discovery!) |
| **View employee list** | `cmd=vemployees` (discovery!) |
| **Export/backup** | Custom commands? |
| **Configuration change** | Config-related commands? |

---

### 2.3 Analysis

**For Each Captured Request:**

1. Extract `cmd=*` value
2. Note any query parameters (`&dataset=`, `&filter=`, etc.)
3. If body present, extract and pretty-print XML
4. If response contains new data types (new XML elements), document

**Output CSV Format:**
```csv
timestamp,command,dataset,has_body,request_size_bytes,response_size_bytes,response_type,new_endpoint
2026-06-15T10:30:01Z,cmd=validate,NULL,FALSE,65,234,xml,FALSE
2026-06-15T10:30:02Z,cmd=vPLUs,NULL,TRUE,412,15234,xml,FALSE
2026-06-15T10:30:45Z,cmd=vfuel,NULL,FALSE,45,892,xml,TRUE
2026-06-15T10:30:50Z,cmd=vrefinteg,ageValidations,FALSE,67,456,xml,FALSE
2026-06-15T10:30:55Z,cmd=vinventory,NULL,FALSE,45,2345,xml,TRUE
...
```

---

## Phase 3: Documentation & Official Channels

### 3.1 Request Official Specs from Verifone

**Contact:** Verifone Technical Support / Sapphire API Team

**Email Template:**
```
Subject: Sapphire NAXML API Endpoint Documentation Request

Dear Verifone Support,

We are developing a third-party management tool for Verifone Commander POS 
controllers using the Sapphire HTTP/NAXML API. We have successfully implemented
the following endpoints:

- cmd=validate (authentication)
- cmd=vPLUs (fetch price look-ups)
- cmd=uPLUs (update price look-ups)
- cmd=vposcfg (fetch departments)
- cmd=vpaymentcfg (fetch tax rates)
- cmd=vrefinteg&dataset=ageValidations (fetch age validations)

We are seeking the official documentation for:
1. Complete list of all available cmd= values
2. All supported datasets for cmd=vrefinteg
3. Request/response schemas (XSD or equivalent) for each endpoint
4. Rate limiting and timeout requirements
5. Authentication/session management details
6. Any deprecated or planned endpoints

Is there official Sapphire NAXML API documentation available?
If not, are there known partners or integrators who can share implementation details?

Thank you,
[Your Name]
```

### 3.2 Public Documentation Review

**Check:**
- Verifone dev portal (if exists)
- Sapphire API GitHub (if public)
- POS system manuals (deployment guide)
- Third-party forums/blogs (other developers)

**Search Terms:**
- "Verifone Sapphire API"
- "Verifone NAXML"
- "Verifone Commander integration"
- "Verifone POS API documentation"

---

## Phase 4: Parametric Discovery (vrefinteg Datasets)

### 4.1 Dataset Enumeration

**Objective:** Find all valid `dataset=` values for `cmd=vrefinteg`.

**Known Datasets:**
- ✅ `ageValidations` (confirmed in code)

**Candidate Datasets:**
| Dataset | Confidence | Reasoning |
|---------|-----------|-----------|
| `allergens` | HIGH | Food industry regulatory; common POS feature |
| `restrictions` | HIGH | Dietary/religious restrictions; marketing feature |
| `flags` | MEDIUM | Internal PLU flags/attributes |
| `fees` | MEDIUM | Surcharge/fee reference data |
| `departments` | LOW | But `vposcfg` already provides this |
| `taxRates` | LOW | But `vpaymentcfg` already provides this |
| `vendors` | LOW | Supplier management |
| `certifications` | LOW | Organic, fair-trade, etc. |
| `origins` | LOW | Product origin (domestic/import) |

**Probing Algorithm:**
```csharp
public async Task<List<string>> DiscoverDatasets()
{
    var candidates = new[]
    {
        "allergens", "restrictions", "flags", "fees",
        "departments", "taxRates", "vendors", "certifications", "origins"
    };
    
    var validDatasets = new List<string>();
    
    foreach (var dataset in candidates)
    {
        try
        {
            var response = await client.SendAsync(
                $"cmd=vrefinteg&dataset={dataset}",
                null,
                cancellationToken);
            
            if (!response.Contains("faultCode") && !response.Contains("unknown"))
            {
                validDatasets.Add(dataset);
            }
        }
        catch { /* continue */ }
    }
    
    return validDatasets;
}
```

---

## Phase 5: Request Parameter Discovery

### 5.1 Parameterized Query Research

**Objective:** Discover query parameters beyond `cmd=`, `cookie=`, `dataset=`.

**Examples:**
- `cmd=vreports&dateFrom=2026-01-01&dateTo=2026-06-15`
- `cmd=vreports&groupBy=sku`
- `cmd=vinventory&departmentId=1`
- `cmd=vinventory&filter=lowStock`

**Probing Strategy:**

For each candidate endpoint (e.g., `vreports`), test common parameter names:

```csharp
var commonParams = new[]
{
    "dateFrom", "dateTo", "startDate", "endDate",
    "groupBy", "orderBy", "sortBy", "filter",
    "departmentId", "skuId", "limit", "offset",
    "pageSize", "page", "format", "fields"
};

foreach (var param in commonParams)
{
    var cmd = $"cmd=vreports&{param}=value1";
    var response = await client.SendAsync(cmd, null, cancellationToken);
    
    // If response differs from base cmd=vreports, parameter is recognized
    if (!IsUnrecognizedParameterError(response))
    {
        results.Add(new ParameterDiscovery
        {
            Endpoint = "vreports",
            Parameter = param,
            Status = "Recognized"
        });
    }
}
```

---

## Phase 6: Results Compilation

### 6.1 Output Documents

#### UNKNOWN_ENDPOINTS_PROBED.md
Lists all endpoints tested and results:

```markdown
# Probed Endpoints Results

Date: 2026-06-15
Environment: Verifone Commander on 192.168.1.50
Session: {cookie}

## HIGH Confidence Probes

| Command | Status | Response Size | Duration | Notes |
|---------|--------|---------------|----------|-------|
| cmd=vfuel | ✅ FOUND | 1,024 bytes | 142 ms | Valid XML response |
| cmd=vinventory | ✅ FOUND | 5,234 bytes | 187 ms | Valid XML response |
| cmd=vplusfuel | ❌ NOT FOUND | 256 bytes | 98 ms | Fault: unknown command |

## MEDIUM Confidence Probes

| Command | Status | Response Size | Duration | Notes |
| ... |

## Summary

- Total Probes: 20
- Found: 3 (includes confirmed 6)
- Not Found: 14
- Error/Timeout: 3
```

#### UNKNOWN_ENDPOINTS_DISCOVERY_RESULTS.json
Machine-readable format for further analysis:

```json
{
  "probeMetadata": {
    "timestamp": "2026-06-15T10:30:00Z",
    "hostnameProbed": "192.168.1.50",
    "totalCandidates": 20,
    "successRate": 0.15,
    "averageResponseTimeMs": 145
  },
  "newEndpointsDiscovered": [
    {
      "command": "vfuel",
      "confidence": "HIGH",
      "responseSize": 1024,
      "responseType": "xml",
      "responsePreview": "<?xml version=\"1.0\"?><fuel><fuelGrade ...>"
    },
    {
      "command": "vinventory",
      "confidence": "HIGH",
      "responseSize": 5234,
      "responseType": "xml",
      "responsePreview": "<?xml version=\"1.0\"?><inventory><item ...>"
    }
  ],
  "notFound": [
    {
      "command": "vplusfuel",
      "reason": "unknown command"
    }
  ]
}
```

#### DISCOVERED_ENDPOINTS_UPDATED.md
Updated version of DISCOVERED_ENDPOINTS.md with new findings:

```markdown
# Discovered Endpoints (Updated)

**Date:** 2026-06-15  
**Original Count:** 6 confirmed  
**New Count:** {count}  
**Discovery Method:** Automated probing + reverse engineering

## Previously Confirmed (6)

1. cmd=validate
2. cmd=vPLUs
3. cmd=uPLUs
4. cmd=vposcfg
5. cmd=vpaymentcfg
6. cmd=vrefinteg (with dataset=ageValidations)

## Newly Discovered

### High Confidence (2–3)

- **cmd=vfuel** — Fuel price management (discovered 2026-06-15)
- **cmd=vinventory** — Inventory/stock levels (discovered 2026-06-15)

### Additional Datasets for cmd=vrefinteg

- **dataset=allergens** — Food allergen information (discovered 2026-06-15)
- **dataset=restrictions** — Dietary restrictions (discovered 2026-06-15)

## Total: 8–10 endpoints confirmed
```

---

## Phase 7: Roadmap & Timeline

| Phase | Activity | Duration | Owner | Output |
|-------|----------|----------|-------|--------|
| 1 | Automated probing (HIGH/MEDIUM candidates) | 1–2 hrs | Dev | PROBED_RESULTS.csv |
| 2 | Network reverse engineering (live POS) | 2–4 hrs | QA | CAPTURED_TRAFFIC.pcap, CSV |
| 3 | Official documentation request | 1–7 days | PM | Email log, docs (if obtained) |
| 4 | vrefinteg dataset enumeration | 30 min | Dev | DATASETS_FOUND.txt |
| 5 | Parameter discovery | 1–2 hrs | Dev | PARAMETERS_FOUND.csv |
| 6 | Results compilation | 2–3 hrs | Dev | Updated .md files |
| 7 | Analysis & recommendations | 1–2 hrs | Tech Lead | ENDPOINT_ANALYSIS.md |

**Total Estimated Effort:** 8–20 hours (depending on POS availability & Verifone responsiveness)

---

## Success Criteria

### Minimum Success
- ✅ Probing completes without errors
- ✅ At least 2 new endpoints confirmed (vfuel, vinventory)
- ✅ Results documented in markdown

### Moderate Success
- ✅ Above + reverse engineering captures 10+ new commands
- ✅ Additional vrefinteg datasets enumerated
- ✅ Parameter discovery reveals filtering/pagination

### Full Success
- ✅ Above + official Verifone specs obtained
- ✅ 5+ new endpoints confirmed
- ✅ Complete API surface documented
- ✅ XSD or formal schema available

---

## Appendix: Tools & Resources

### Probing Tool (Console App)

```csharp
// Program.cs
class SapphireDiscoveryTool
{
    static async Task Main(string[] args)
    {
        var hostname = args[0];  // e.g., "192.168.1.50"
        var username = args[1];  // e.g., "admin"
        var password = args[2];  // e.g., "password"
        
        var credentialProvider = new SapphireCredentialProvider(...);
        credentialProvider.SetLoginCredentials(hostname, username, password);
        
        var client = new SapphireClient(...);
        var prober = new EndpointProber(client);
        
        var candidates = LoadCandidates("candidates.txt");
        var results = await prober.ProbeEndpointsAsync(candidates, TimeSpan.FromSeconds(30), CancellationToken.None);
        
        ExportResults(results, "probed_results.csv");
        
        Console.WriteLine($"Probed: {results.Total}, Found: {results.Found}, Not Found: {results.NotFound}");
    }
}
```

### Reverse Engineering Checklist

- [ ] Burp Suite / Fiddler installed and configured
- [ ] Proxy certificate trusted in Windows
- [ ] Price Book Manager configured to use proxy
- [ ] `AllowUntrustedCertificates = true` (temporary)
- [ ] HTTP history recording started
- [ ] POS admin interface accessed
- [ ] Operations performed (upload prices, manage inventory, etc.)
- [ ] HTTP traffic captured to file
- [ ] Traffic analyzed for new cmd= values
- [ ] `AllowUntrustedCertificates` reverted to false

### Expected Findings

**Realistic Scenario (Conservative):**
- 2–3 new endpoints confirmed
- 5–10 new vrefinteg datasets
- 1–2 parameterized queries
- Total endpoints: 11–16

**Optimistic Scenario:**
- 5–7 new endpoints confirmed
- 10+ vrefinteg datasets
- 5+ parameterized queries + filters
- Total endpoints: 15–25

**Pessimistic Scenario:**
- 0 new endpoints discovered
- Verifone blocks probing or doesn't respond to requests
- POS doesn't support additional cmd= values
- Discovery plan fails; manual documentation request becomes critical

---

## Document Control

| Version | Date | Status | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-06-15 | Complete | Initial discovery plan (7 phases, comprehensive methodology) |
