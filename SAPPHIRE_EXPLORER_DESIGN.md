# Sapphire Explorer Mode Design

**Date:** 2026-06-15  
**Author:** Technical Reconnaissance  
**Purpose:** Feasibility study for transforming Verifone Commander Price Book Manager into a generic Sapphire API debugging/discovery tool  
**Status:** Design (no code changes)

---

## Executive Summary

The Verifone Commander Price Book Manager can be extended with a **Sapphire Explorer Mode** — a developer/tester interface for:

- Sending arbitrary Sapphire commands (`cmd=` values)
- Entering custom dataset parameters
- Composing raw NAXML request bodies
- Viewing prettified XML responses
- Saving response artifacts for analysis
- Exporting discoveries to XML/JSON

**Key Finding:** The codebase is already architected to support this. The `SapphireClient.SendAsync()` method accepts a generic `cmdHeader` parameter; no hardcoding forces specific commands. Architecture can be extended with minimal changes.

---

## 1. Exact Technical Specifications

### 1.1 Login URL Format

**Specification (Exact):**
```
https://{user_configured_hostname}/cgi-bin/NAXML
```

**Source:** `src/Core/SapphireCredentialProvider.cs:18, 59`
```csharp
private const string CgiBinNaxmlPath = "/cgi-bin/NAXML";
this.requestUri = new Uri("https://" + hostName + CgiBinNaxmlPath);
```

**Details:**
- **Protocol:** HTTPS only (no HTTP fallback)
- **Hostname:** User-provided via login UI (`SetLoginCredentials()` parameter)
- **Port:** 443 (implicit; not overrideable in current code)
- **Path:** Hardcoded to `/cgi-bin/NAXML`
- **URL Construction:** String concatenation: `"https://" + hostname + "/cgi-bin/NAXML"`

**Hostname Examples (from code):**
- `192.168.31.11` (mock example in test code)
- `10.0.0.50` (typical RFC1918 POS address)
- `commander.local` (DNS name example)

**Limitation:** No custom port support; always assumes 443.

---

### 1.2 Exact NAXML Request Envelope Structure

**Specification (Exact from source):**

#### Authentication Request (cmd=validate)
```
POST /cgi-bin/NAXML HTTP/1.1
Host: {hostname}
Content-Type: text/plain; charset=UTF-8
Content-Length: {length}

cmd=validate&user={username}&passwd={password}
```

**Source:** `src/Core/SapphireCredentialProvider.cs:69-71`
```csharp
using var request = SapphireHttpUtil.CreateRequest(
    this.requestUri,
    $"cmd=validate&user={this.username}&passwd={this.password}");
```

#### Standard Request (with body)
```
POST /cgi-bin/NAXML HTTP/1.1
Host: {hostname}
Content-Type: text/plain; charset=UTF-8
Content-Length: {length}

cmd={command}&cookie={session_cookie}

{optional_xml_body}
```

**Source:** `src/Core/SapphireClient.cs:248-261` (GetRequestBody)
```csharp
static string GetRequestBody(string cmdHeader, string body, string cookie)
{
    var sb = new StringBuilder();
    sb.Append(cmdHeader);           // e.g., "cmd=vPLUs"
    sb.Append("&cookie=");
    sb.AppendLine(cookie);          // Session cookie from cmd=validate
    
    if (!string.IsNullOrEmpty(body))
    {
        sb.AppendLine();
        sb.Append(body);            // Optional NAXML XML body
    }
    
    return sb.ToString();
}
```

#### Query-Only Request (no body)
```
POST /cgi-bin/NAXML HTTP/1.1
Host: {hostname}
Content-Type: text/plain; charset=UTF-8
Content-Length: {length}

cmd={command}&cookie={session_cookie}
```

**Request Body Construction:**
1. **cmdHeader:** e.g., `"cmd=vPLUs"` (query) or `"cmd=vrefinteg&dataset=ageValidations"` (parameterized)
2. **cookie:** Session token obtained from prior `cmd=validate` response
3. **Separator:** `&cookie=` + `\r\n` (via `AppendLine()`)
4. **Body:** Optional NAXML XML (separated by blank line `\r\n\r\n`)

**Content-Type:** Always `text/plain; charset=UTF-8`

**Source:** `src/Core/SapphireHttpUtil.cs:31-38`
```csharp
public static HttpRequestMessage CreateRequest(Uri requestUri, string content)
{
    return new HttpRequestMessage(HttpMethod.Post, requestUri)
    {
        Content = new StringContent(content, Encoding.UTF8, "text/plain"),
    };
}
```

---

### 1.3 Exact HTTP Headers Required

**Specification (Exact):**

| Header | Value | Source | Required |
|--------|-------|--------|----------|
| `Host` | {hostname}:443 | URI construction | Yes |
| `Content-Type` | `text/plain; charset=UTF-8` | `SapphireHttpUtil.cs:38` | Yes |
| `Content-Length` | {length of body} | HttpClient auto (POST) | Auto |
| `User-Agent` | `.NET HttpClient/{version}` | HttpClient default | Auto |

**Optional Headers (defaults applied):**
| Header | Current Value | Location | Note |
|--------|---------------|----------|------|
| `Connection` | HTTP/1.1 keep-alive | HttpClient default | Auto-managed |
| `Accept` | `*/*` | HttpClient default | Auto |
| `Accept-Encoding` | `gzip, deflate` | HttpClient default | Auto |

**Custom Headers in Code:**
- None observed in `SapphireClient.cs` or `SapphireCredentialProvider.cs`
- All requests use default HttpClient headers
- No authentication headers (auth via query param `&cookie=`)

**Security Headers (NOT present):**
- `Authorization` — Not used; cookie-based instead
- `X-CSRF-Token` — Not observed
- `Accept-Encoding: identity` — Not set (compression allowed)

**TLS Validation:**
- **Default Behavior:** Standard HTTPS certificate validation (hostname match, trusted CA)
- **Override Available:** `HttpClientHandler.ServerCertificateCustomValidationCallback` in `HttpClientHttpRequestSender.cs:28`
- **Current Default:** Certificate validation enabled
- **User Toggle:** `Settings.AllowUntrustedCertificates` (true = accept self-signed)

---

### 1.4 Exact Cookie Handling Mechanism

**Specification (Exact):**

#### Storage
- **Type:** Opaque string (no JWT or structured format)
- **Location:** In-memory variable (`SapphireCredentialProvider.cs:25`)
  ```csharp
  private string cookie;
  ```
- **Lifetime:** Until session expiry or app close
- **Persistence:** None (not saved to disk; lost on app restart)

#### Acquisition Flow

**Step 1: Initiate Login**
```csharp
// User enters hostname, username, password in UI
SetLoginCredentials(hostname, username, password);
```

**Step 2: Lazy Load Cookie**
```csharp
// On first API call, cookie is fetched
var credentials = await credentialProvider.GetCredentialsAsync(cancellationToken);
```

**Step 3: Send cmd=validate Request**
```
POST https://{hostname}/cgi-bin/NAXML
Content-Type: text/plain

cmd=validate&user={username}&passwd={password}
```

**Step 4: Parse Cookie from Response**
```csharp
var doc = XDocument.Parse(responseContent);
this.cookie = doc.Descendants("cookie").First().Value;
```

**Source:** `src/Core/SapphireCredentialProvider.cs:90`
```csharp
var doc = XDocument.Parse(responseContent);
this.cookie = doc.Descendants("cookie").First().Value;
```

**Response Format (Expected):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<sapphire>
  <cookie>ABC123DEF456789XYZ...</cookie>
</sapphire>
```

#### Reuse

Every subsequent request automatically includes the cached cookie:
```
cmd={command}&cookie={cached_value}
```

#### Refresh

**Automatic Refresh Trigger:**
- Cookie is not checked for expiry in code
- POS device validates cookie; if expired, returns error
- On error, application throws `SapphireRequestException`
- UI handles exception (no automatic retry)

**Manual Refresh:**
```csharp
// Reset cookie in provider
this.cookie = null;  // Cached in memory

// Next API call will re-authenticate
var credentials = await credentialProvider.GetCredentialsAsync(...);
// Calls cmd=validate again if cookie is null
```

**Source:** `src/Core/SapphireCredentialProvider.cs:60`
```csharp
this.cookie = null;  // Reset on SetLoginCredentials()
```

#### No Timeout Mechanism

**Current Behavior:** No explicit timeout; relies on POS to reject expired cookies.

**Risk:** If cookie expires on POS but app doesn't detect it, next request fails with cryptic error.

---

### 1.5 Arbitrary Command Support

**Question:** Can arbitrary `cmd=` values be sent without code changes?

**Answer:** ✅ **YES — Fully Supported by Architecture**

**Evidence:**

The `SapphireClient.SendAsync()` method accepts a generic `cmdHeader` parameter:

**Source:** `src/Core/SapphireClient.cs:210-222`
```csharp
private async Task<string> SendAsync(
    string cmdHeader,    // ← Generic parameter, not hardcoded
    string body,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(cmdHeader))
    {
        throw new ArgumentException($"'{nameof(cmdHeader)}' cannot be null or whitespace.", nameof(cmdHeader));
    }

    var credentials = await this.credentialProvider.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
    var requestContent = GetRequestBody(cmdHeader, body, credentials.Cookie);
    
    // ... sends request with cmdHeader as-is
}
```

**Validation:** Only non-null/non-whitespace check; no hardcoded command list.

**Current Usage (Hardcoded Calls):**
```csharp
// These are hardcoded in public methods, but SendAsync() itself accepts any cmdHeader
await this.SendAsync(cmdHeader: "cmd=vPLUs", body: body, ...);
await this.SendAsync(cmdHeader: "cmd=uPLUs", body: body, ...);
await this.SendAsync(cmdHeader: "cmd=vposcfg", body: null, ...);
await this.SendAsync(cmdHeader: "cmd=vrefinteg&dataset=ageValidations", body: null, ...);
```

**To Send Arbitrary Commands:**

Option 1 (Minimal): Add public method to `SapphireClient`:
```csharp
public async Task<string> SendCustomCommandAsync(
    string cmdHeader,
    string body,
    CancellationToken cancellationToken)
{
    return await this.SendAsync(cmdHeader, body, cancellationToken);
}
```

Option 2 (Wrapper): Create `ExplorerClient` wrapper:
```csharp
public class SapphireExplorerClient
{
    private readonly ISapphireClient client;
    
    public async Task<string> ExploreAsync(string cmd, string dataset = null, string body = null)
    {
        var cmdHeader = cmd;
        if (!string.IsNullOrEmpty(dataset))
            cmdHeader += $"&dataset={dataset}";
        
        return await ((SapphireClient)client).SendCustomCommandAsync(cmdHeader, body, cancellationToken);
    }
}
```

**Constraint:** `SendAsync()` is currently `private`; would need visibility change to `public` or `protected`.

---

### 1.6 Generic Sapphire Explorer Feasibility

**Question:** Can the application be modified into a generic Sapphire Explorer?

**Answer:** ✅ **YES — High Feasibility, Low Implementation Cost**

#### Architectural Fit

The app already has all required layers:

| Layer | Component | Reuse for Explorer |
|-------|-----------|-------------------|
| **HTTP/TLS** | `HttpClientHttpRequestSender` | ✅ Full reuse |
| **Auth** | `SapphireCredentialProvider` | ✅ Full reuse |
| **API Communication** | `SapphireClient.SendAsync()` | ✅ Needs visibility change only |
| **XML Parsing** | `XDocument.Parse()` | ✅ Full reuse |
| **Error Handling** | `SapphireRequestException` | ✅ Full reuse |

#### Required Modifications (Minimal)

1. **Visibility Change:** `SapphireClient.SendAsync()` from `private` → `public`
2. **New UI Pages:** Explorer page in `src/DesktopApp/` (XAML + ViewModel)
3. **New ViewModel:** `ExplorerPageVm` (cmd/dataset/body input, response display)
4. **Export Utilities:** XML/JSON output formatting (new file)

**Estimated LOC:** 400–600 lines (UI + logic)

#### Architectural Compatibility

**Current:** Single-purpose app (PLU editor)
- Main navigation: Account → Search → Edit → Bulk Operations → Settings
- Cache: Global, updated on login/refresh
- Data flow: API → cache → UI model → UI display

**With Explorer:** Multi-purpose app (PLU editor + API explorer)
- New tab: `ExplorerPage`
- No cache requirement (single-request model)
- Data flow: User input → raw API call → raw response display

**Conflicts:** None. Explorer operates independently.

---

## 2. Sapphire Explorer Features

### 2.1 Core Features (MVP)

#### Feature 1: Arbitrary Command Entry

**UI Component:**
```
┌─────────────────────────────────────────┐
│ Sapphire Explorer                        │
├─────────────────────────────────────────┤
│ Command:  [________________] (e.g., vfuel)│
│ Dataset:  [________________] (optional)   │
│ [Send Request Button]                    │
└─────────────────────────────────────────┘
```

**Input Fields:**
- **cmd:** Text field (auto-complete suggestions: vPLUs, vposcfg, vfuel, vinventory, etc.)
- **dataset:** Optional text field (shown only if cmd=vrefinteg)
- **Send Button:** Triggers API call

**Validation:**
- cmd is required, non-empty
- dataset is optional
- Auto-prepend `cmd=` if user enters just `vfuel`

**Code:**
```csharp
public async Task SendCommand(string cmd, string dataset)
{
    var cmdHeader = cmd;
    if (!string.IsNullOrEmpty(dataset))
        cmdHeader += $"&dataset={dataset}";
    
    var response = await sapphireClient.SendAsync(cmdHeader, null, cancellationToken);
    this.ResponseXml = response;
}
```

#### Feature 2: Custom NAXML Body Entry

**UI Component:**
```
┌─────────────────────────────────────────┐
│ Request Body (NAXML)                    │
├─────────────────────────────────────────┤
│ <domain:PLUs                            │
│   xmlns:domain="...">                   │
│   <domain:PLU>                          │
│     ...                                 │
│   </domain:PLU>                         │
│ </domain:PLUs>                          │
│                                         │
│ [Send Request Button]                   │
└─────────────────────────────────────────┘
```

**Input Field:**
- **Body:** Multi-line text editor (XML syntax highlighting optional)
- Preserves formatting
- Optional; used for `cmd=uPLUs`, `cmd=uFuel`, etc.

**Validation:**
- Validates XML well-formedness (basic check: parse attempt)
- Shows error if invalid XML

**Code:**
```csharp
public async Task SendWithBody(string cmd, string xmlBody)
{
    try
    {
        XDocument.Parse(xmlBody);  // Validate
        var response = await sapphireClient.SendAsync(cmd, xmlBody, cancellationToken);
        this.ResponseXml = response;
    }
    catch (XmlException ex)
    {
        this.ErrorMessage = $"Invalid XML: {ex.Message}";
    }
}
```

#### Feature 3: Raw XML Response Viewer

**UI Component:**
```
┌─────────────────────────────────────────┐
│ Response (Raw XML)                      │
├─────────────────────────────────────────┤
│ <domain:PLUs page="1" ofPages="1">      │
│   <domain:PLU>                          │
│     <upc>5901234123457</upc>            │
│     ...                                 │
│   </domain:PLU>                         │
│ </domain:PLUs>                          │
│                                         │
│ [Copy] [Format] [Save]                  │
└─────────────────────────────────────────┘
```

**Display:**
- Raw XML from API response
- Read-only text box (optional: syntax highlighting)
- Status: Success, error, exception

**Features:**
- **Copy Button:** Copy full response to clipboard
- **Format Button:** Pretty-print XML (indent + line breaks)
- **Save Button:** Save to file (UI selects location)

**Code:**
```csharp
public void FormatResponse()
{
    try
    {
        var doc = XDocument.Parse(this.ResponseXml);
        this.ResponseXml = doc.ToString();  // Pretty-printed
    }
    catch { /* already formatted */ }
}

public void SaveResponse()
{
    var filePath = dialogService.SaveFileDialog("*.xml");
    if (filePath != null)
    {
        File.WriteAllText(filePath, this.ResponseXml);
    }
}
```

#### Feature 4: Response Metadata

**Display:**
```
│ HTTP Status: 200 OK
│ Content-Type: text/plain; charset=UTF-8
│ Content-Length: 1234 bytes
│ Time: 145 ms
│ Timestamp: 2026-06-15 10:30:45 UTC
```

**Captured:**
- HTTP status code
- Response headers (Content-Type, Content-Length)
- Round-trip time (ms)
- Timestamp (UTC)
- Full exception message if failed

---

### 2.2 Advanced Features (Phase 2)

#### Feature 5: Saved Requests Library

**Purpose:** Store + reuse common queries

**UI:**
```
┌─ Saved Requests ─────────────────────────┐
│ [+ New] [Delete] [Edit]                  │
├──────────────────────────────────────────┤
│ □ Fetch All PLUs                         │
│ □ Fetch Departments                      │
│ □ Fetch Age Validations                  │
│ □ Test vfuel Endpoint                    │
│ □ List Inventory                         │
└──────────────────────────────────────────┘
```

**Storage:** Local JSON file (`~/.sapphire-explorer/requests.json`)

**Schema:**
```json
{
  "savedRequests": [
    {
      "id": "uuid",
      "name": "Fetch All PLUs",
      "cmd": "vPLUs",
      "dataset": null,
      "body": "<domain:PLUSelect xmlns:domain=\"urn:vfi-sapphire:np.domain.2001-07-01\">\n  <pageSize>1000000</pageSize>\n  <page>1</page>\n</domain:PLUSelect>",
      "createdAt": "2026-06-15T10:30:00Z",
      "lastUsed": "2026-06-15T10:30:45Z"
    }
  ]
}
```

**Code:**
```csharp
public class SavedRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
    public string Cmd { get; set; }
    public string Dataset { get; set; }
    public string Body { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsed { get; set; }
}
```

#### Feature 6: Response Diff/Comparison

**Purpose:** Compare responses from different endpoints or timestamps

**UI:**
```
Request 1 (2026-06-15 10:30)    vs    Request 2 (2026-06-15 10:31)
<domain:PLUs ...>                      <domain:PLUs ...>
  <domain:PLU>                           <domain:PLU>
    <upc>5901234123457</upc>  ─┐         <upc>5901234123457</upc>
    <price>9.99</price>       ├─ DIFF   <price>10.99</price>  ◄─ CHANGED
    <description>Coffee</...  ─┘         <description>Coffee</...
  </domain:PLU>                         </domain:PLU>
</domain:PLUs>                          </domain:PLUs>
```

#### Feature 7: Endpoint Autodiscovery

**Purpose:** Probe for unknown endpoints

**UI:**
```
┌─ Autodiscovery ──────────────────────────┐
│ [Start Scan]                             │
├──────────────────────────────────────────┤
│ Testing: vfuel             [✓ Found]    │
│ Testing: vplusfuel         [✗ Not found]│
│ Testing: vinventory        [↔ Pending]  │
│ Testing: vstock            [○ Skipped]  │
└──────────────────────────────────────────┘
```

**Algorithm:**
1. Read candidate list from POSSIBLE_ENDPOINTS.md
2. For each candidate, send `cmd={candidate}` with minimal body
3. Check response for success vs. "unknown command" fault
4. Log results to file

**Code:**
```csharp
public async Task DiscoverEndpoints(List<string> candidates)
{
    var discoveries = new List<EndpointProbeResult>();
    
    foreach (var cmd in candidates)
    {
        try
        {
            var response = await sapphireClient.SendAsync($"cmd={cmd}", null, cancellationToken);
            var isSuccess = !response.Contains("VFI:Fault") && !response.Contains("faultCode");
            discoveries.Add(new EndpointProbeResult
            {
                Command = cmd,
                Status = isSuccess ? "Found" : "Not Found",
                Response = response
            });
        }
        catch (Exception ex)
        {
            discoveries.Add(new EndpointProbeResult
            {
                Command = cmd,
                Status = "Error",
                Error = ex.Message
            });
        }
    }
    
    // Export results
    await ExportDiscoveries(discoveries);
}
```

---

### 2.3 Export Features

#### Export Formats

| Format | Purpose | Use Case |
|--------|---------|----------|
| **XML** | Raw response | Archive, analysis, diff tools |
| **JSON** | Structured data | Import into analytics tools |
| **CSV** | Tabular data | Excel, database import |
| **HTML** | Human-readable | Email, documentation |
| **YAML** | Config-like | Documentation, version control |

#### Export Options UI

```
┌─ Export Response ──────────────────────────┐
│ Format: [XML    ▼]                         │
│ Filename: [response-vPLUs-2026-06-15.xml]  │
│ Include Metadata: [✓]                      │
│                                            │
│ [Browse...] [Export]                       │
└────────────────────────────────────────────┘
```

---

## 3. Implementation Architecture

### 3.1 New Classes Required

```csharp
// ExplorerPageVm.cs
public class ExplorerPageVm : ViewModelBase
{
    // Input properties
    public string CommandName { get; set; }
    public string DatasetName { get; set; }
    public string RequestBody { get; set; }
    
    // Output properties
    public string ResponseXml { get; set; }
    public string ResponseMetadata { get; set; }
    public string ErrorMessage { get; set; }
    
    // Methods
    public async Task SendRequest();
    public void FormatResponse();
    public void SaveResponse();
    public void CopyResponse();
}

// SapphireExplorerClient.cs
public class SapphireExplorerClient
{
    private readonly ISapphireClient client;
    
    public async Task<ExplorerResponse> SendCustomCommandAsync(
        string cmd,
        string dataset,
        string body,
        CancellationToken cancellationToken);
}

// ExplorerResponse.cs
public class ExplorerResponse
{
    public string ResponseXml { get; set; }
    public int HttpStatusCode { get; set; }
    public DateTime Timestamp { get; set; }
    public long DurationMs { get; set; }
    public Dictionary<string, string> Headers { get; set; }
    public Exception Exception { get; set; }
    public bool IsSuccess { get; set; }
}
```

### 3.2 UI Page Structure

```
src/DesktopApp/ExplorerPage.xaml
├─ Header Section
│  ├─ Title: "Sapphire API Explorer"
│  └─ Subtitle: "Send custom commands to the live POS device"
│
├─ Input Section (Left Panel)
│  ├─ Command Entry (ComboBox with autocomplete)
│  ├─ Dataset Entry (TextBox, conditional show)
│  ├─ Request Body (Multi-line TextBox with XML editor)
│  └─ Buttons: [Send] [Clear] [Load from Saved] [Save]
│
├─ Response Section (Right Panel)
│  ├─ Tabs:
│  │  ├─ Raw XML
│  │  ├─ Formatted XML
│  │  ├─ Metadata (status, headers, timing)
│  │  └─ Errors
│  │
│  └─ Buttons: [Copy] [Save] [Export ▼]
│
└─ Footer
   ├─ Status: "Connected" / "Ready" / "Error"
   └─ Last Request Time: "145 ms"
```

### 3.3 Visibility Changes Required

**File:** `src/Core/SapphireClient.cs`

**Change:**
```csharp
// Before
private async Task<string> SendAsync(string cmdHeader, string body, CancellationToken cancellationToken)

// After (Option 1: Make public)
public async Task<string> SendAsync(string cmdHeader, string body, CancellationToken cancellationToken)

// After (Option 2: Create public wrapper)
public async Task<string> SendCustomCommandAsync(string cmdHeader, string body, CancellationToken cancellationToken)
{
    return await this.SendAsync(cmdHeader, body, cancellationToken);
}
```

**Impact:** Low — no signature changes, just visibility.

---

## 4. Integration Points

### 4.1 Navigation Menu Integration

**Current Navigation:**
```
Account
Search
Edit
Bulk Operations
Settings
```

**With Explorer:**
```
Account
Search
Edit
Bulk Operations
⭐ API Explorer (NEW)
Settings
```

**Code (MainNavigationView.xaml.cs):**
```xml
<NavigationView.MenuItems>
    <NavigationViewItem Content="Account" Tag="Account" Icon="Home" />
    <NavigationViewItem Content="Search" Tag="Search" Icon="Find" />
    <NavigationViewItem Content="Edit" Tag="Edit" Icon="Edit" />
    <NavigationViewItem Content="Bulk Operations" Tag="BulkOperations" Icon="AllApps" />
    <NavigationViewItem Content="API Explorer" Tag="Explorer" Icon="Code" />  <!-- NEW -->
    <NavigationViewItem Content="Settings" Tag="Settings" Icon="Setting" />
</NavigationView.MenuItems>
```

### 4.2 Shared State

**Session Credentials:** Reuse from existing login
- No new login required
- Explorer uses same `credentialProvider`
- Cookie shared across tabs

**Cache:** Explorer does NOT share cache
- Each request is independent
- No cache pollution
- No side effects on PLU editor

### 4.3 Settings Integration

**New Settings:**
```json
{
  "explorersettings": {
    "autoFormatResponses": true,
    "saveResponseHistory": true,
    "maxHistorySizeBytes": 104857600,
    "requestTimeout": 30000
  }
}
```

---

## 5. Security Considerations

### 5.1 Input Validation

| Input | Validation | Risk |
|-------|-----------|------|
| **cmd** | Non-empty; alphanumeric + `_` + `=` + `&` | Medium: Command injection into query string |
| **dataset** | Non-empty; alphanumeric + `_` | Low: Limited character set |
| **body** | XML well-formedness check | Low: Pre-validated before send |

**Mitigation:**
```csharp
public void ValidateCommand(string cmd)
{
    if (string.IsNullOrEmpty(cmd))
        throw new ArgumentException("Command cannot be empty");
    
    if (!Regex.IsMatch(cmd, @"^[a-zA-Z0-9_=&]+$"))
        throw new ArgumentException("Command contains invalid characters");
}
```

### 5.2 Response Handling

| Risk | Mitigation |
|------|-----------|
| **XXE (XML External Entity)** | Use `XDocument.Parse()` with DTD disabled (default in .NET 6+) |
| **Response Size DoS** | Optional: Set max response size (e.g., 100 MB) |
| **Cookie Exposure** | Never log/display cookie; use `[REDACTED_COOKIE]` |
| **Error Message Disclosure** | Filter sensitive data from error UI |

**Code:**
```csharp
// Redact cookie from logs
var safeRequest = GetRequestBody(cmd, body, "[REDACTED_COOKIE]");
logger.LogInformation("Request: {0}", safeRequest);
```

### 5.3 Privilege Escalation

**Risk:** Explorer allows any authenticated user to issue commands

**Mitigation:**
- Feature flag in settings: `EnableExplorer: false` (default)
- Requires explicit opt-in
- Audit log all Explorer commands (future)
- No new authentication layer needed (relies on existing login)

---

## 6. Testing Strategy

### 6.1 Unit Tests

```csharp
[Fact]
public async Task SendCustomCommand_ValidCmd_ReturnsXmlResponse()
{
    // Arrange
    var explorer = new SapphireExplorerClient(mockClient);
    
    // Act
    var response = await explorer.SendCustomCommandAsync("vPLUs", null, null, cancellationToken);
    
    // Assert
    Assert.NotNull(response.ResponseXml);
    Assert.True(response.IsSuccess);
}

[Fact]
public async Task SendCustomCommand_InvalidCmd_ReturnsError()
{
    // Arrange
    var explorer = new SapphireExplorerClient(mockClient);
    
    // Act / Assert
    await Assert.ThrowsAsync<SapphireRequestException>(
        () => explorer.SendCustomCommandAsync("cmd=invalid", null, null, cancellationToken));
}

[Fact]
public void ValidateCommand_ValidInput_NoThrow()
{
    ExplorerValidator.ValidateCommand("vfuel");
    ExplorerValidator.ValidateCommand("vrefinteg&dataset=allergens");
    // No exception
}

[Fact]
public void ValidateCommand_InvalidInput_Throws()
{
    Assert.Throws<ArgumentException>(
        () => ExplorerValidator.ValidateCommand("cmd=$(whoami)"));
}
```

### 6.2 Integration Tests

```csharp
[Fact]
public async Task ExplorerEndToEnd_LivePOS_DiscoversFuel()
{
    // Requires live POS on network
    var response = await explorer.SendCustomCommandAsync("vfuel", null, null, cancellationToken);
    
    // If vfuel exists, response is valid XML
    Assert.True(response.IsSuccess);
    XDocument.Parse(response.ResponseXml);  // No throw
}
```

### 6.3 Manual Testing

- [ ] Login with valid credentials
- [ ] Send `cmd=vPLUs` (known endpoint) → success
- [ ] Send `cmd=unknown123` (invalid) → error
- [ ] Send `cmd=vrefinteg&dataset=ageValidations` (with param) → success
- [ ] Test XML body editing (format, validation)
- [ ] Test export to XML, JSON, CSV
- [ ] Test response diff (if implemented)
- [ ] Test saved requests (if implemented)

---

## 7. Phased Rollout

### Phase 1: MVP (Week 1–2)
- Basic UI (cmd, dataset, body inputs)
- Send request functionality
- Raw XML response display
- Copy/Save buttons

**LOC:** ~300

### Phase 2: Enhancements (Week 3–4)
- Response formatting
- Metadata display
- Response diff
- Export (XML, JSON)

**LOC:** ~200

### Phase 3: Advanced (Week 5–6)
- Saved requests library
- Autodiscovery scanning
- Response history
- Feature flag toggle

**LOC:** ~300

**Total MVP Effort:** ~300–400 LOC  
**Total Full Feature Set:** ~800–1000 LOC

---

## 8. Limitations & Constraints

### 8.1 Limitations

| Limitation | Impact | Workaround |
|-----------|--------|-----------|
| No custom port support | Can't test POS on non-443 | Manual hostname editing |
| No HTTP (HTTPS only) | Can't debug unencrypted traffic | Use proxy tool (Burp) |
| No request templating | Can't parameterize bodies | Saved requests with manual edits |
| Single request at a time | Can't stress-test | Use external tool |
| No WebSocket support | Can't test persistent connections | Not applicable to Sapphire API |

### 8.2 Design Constraints

| Constraint | Reason |
|-----------|--------|
| Reuse existing `SendAsync()` | Minimize code changes |
| No changes to PLU editor | Maintain backward compatibility |
| Feature flag on by default: OFF | Minimize security surface |
| Shared session credentials | Reduce UI friction |

---

## 9. Success Metrics

### 9.1 Functional Goals

- ✅ Send arbitrary `cmd=*` commands
- ✅ Support parameterized queries (`dataset=*`)
- ✅ Accept custom XML bodies
- ✅ Display raw XML responses
- ✅ Export responses to files
- ✅ Discover hidden Sapphire endpoints (via integrated scanning)

### 9.2 Usability Goals

- ✅ Can issue 3 commands without reading docs
- ✅ Response visible within 2 seconds of send
- ✅ Can save/reuse commands in <1 minute

### 9.3 Quality Goals

- ✅ No crashes on invalid input
- ✅ All errors have actionable messages
- ✅ Response time <200 ms for typical commands
- ✅ 0 information disclosure in errors (no credentials, no paths)

---

## 10. Appendix: Code Examples

### 10.1 Command Format Examples

```
# Query command (no body)
cmd=vPLUs
cmd=vposcfg
cmd=vpaymentcfg

# Parameterized query
cmd=vrefinteg&dataset=ageValidations
cmd=vrefinteg&dataset=allergens
cmd=vreports&dateFrom=2026-01-01&dateTo=2026-06-15

# Mutation command (with body)
cmd=uPLUs
cmd=uFuel
cmd=uInventory

# Authentication
cmd=validate&user=admin&passwd=password123
```

### 10.2 Request Body Examples

**Query (vPLUs):**
```xml
<domain:PLUSelect xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01">
  <pageSize>1000000</pageSize>
  <page>1</page>
</domain:PLUSelect>
```

**Mutation (uPLUs - update):**
```xml
<domain:PLUs xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01" page="1" ofPages="1">
  <domain:PLU>
    <upc>5901234123457</upc>
    <upcModifier>000</upcModifier>
    <description>New Description</description>
    <department>1</department>
    <fees><fee>0</fee></fees>
    <pcode>0</pcode>
    <price>10.99</price>
    <flags>
      <domain:flag sysid="1" />
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

---

## Document Control

| Version | Date | Status | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-06-15 | Complete | Initial design (comprehensive feasibility study) |
