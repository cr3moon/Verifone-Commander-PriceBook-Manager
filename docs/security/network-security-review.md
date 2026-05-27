# Network Security Review — Verifone Commander Price Book Manager

> **Status update (2026-05-26):** **Finding 1 (TLS bypass)** is now a
> **secure-by-default, user-controlled setting** — `Settings.AllowUntrustedCertificates`
> (default off). Validation only relaxes when the operator explicitly enables it on the
> Settings page for the self-signed / IP-only Commander controller; the previous
> *unconditional, silent* bypass is gone. Residual risk when the toggle is ON is accepted
> by design (the controller has no FQDN / valid cert). **Findings #3 (response-size DoS),
> #4 (fail-open fault detection), and #5 (default-coercion) remain OPEN.**

**Date:** 2026-05-25
**Scope:** Network-based security risks — data exfiltration, remote outbound / command-and-control (C2) channels, and the trust model of the one network connection the app makes.
**Subject:** WinUI desktop app + console tool + Core library that communicates with a Verifone Commander "Sapphire" POS HTTP API over the local network.
**Method:** Independent (cross-vendor) review via OpenAI Codex, read-only sandbox, in two passes. Every reported finding was re-verified against source by reading the cited lines.

---

## TL;DR / Verdict

**On the requested threat model — data exfiltration and C2 — the codebase is clean.** There is no covert outbound channel anywhere in `./src`: no telemetry/analytics SDK, no crash reporter, no SMTP/DNS callback, no webhook, no raw sockets, no secondary `HttpClient`, no update-checker, no remote script/assembly loading, and no hardcoded external domains. The only destination the app ever constructs is `https://{user-configured-host}/cgi-bin/NAXML` (plus an RFC1918 sample IP in the console tool).

**The real exposure is the opposite direction:** the single legitimate channel to the POS is cryptographically unauthenticated (TLS validation disabled + permissive handler defaults), and the app then *uncritically trusts whatever comes back over that channel*. A network attacker positioned for MITM cannot just eavesdrop — they can crash the app, feed it forged "successful" data, or poison the local cache with bad prices/UPCs that the user may later write back to the POS.

Priority fixes: **#1 (transport trust)**, then **#3 + #4** (response-size DoS and fail-open fault detection), then **#5** (silent default-coercion of response fields).

---

## Pass 1 — Transport Trust (the obvious findings)

### Finding 1 — TLS certificate validation fully disabled — **High**

- **Location:** `src/Core/HttpClientHttpRequestSender.cs:24`
- **Description:** `ServerCertificateCustomValidationCallback = (_, _, _, _) => true` disables all TLS identity checks on the shared `HttpClientHandler`. This is process-wide for the app (both desktop and console route all traffic through this sender). The login request sends `cmd=validate&user=...&passwd=...`; subsequent requests carry `&cookie=...` plus PLU/XML payloads over this weakened channel.
- **Exploit scenario:** An attacker on the same network, a rogue AP, or an intercepting proxy presents any certificate for the Sapphire hostname. The app accepts it and transmits username/password, session cookie, and full price-book operations to the attacker, who can also tamper with returned XML.
- **Remediation:** Remove the unconditional callback. If Sapphire uses self-signed certs, **pin** the expected certificate / public key / thumbprint per configured host (or ship an explicit trust store for the POS device). Fail closed on hostname or certificate mismatch.

### Finding 2 — Permissive `HttpClientHandler` defaults (redirect / proxy) — **High**

- **Location:** `src/Core/HttpClientHttpRequestSender.cs:21`
- **Description:** The bare `HttpClientHandler` leaves `AllowAutoRedirect` and `UseProxy` at their permissive defaults. Sensitive data rides in the POST body (not an origin-bound auth mechanism), so a MITM (enabled by Finding 1), a malicious redirect, or a hostile proxy/PAC/WPAD environment can steer the Sapphire session off-box.
- **Exploit scenario:** Attacker returns a `307/308` redirect to `https://evil-host/...`, or the workstation is forced onto an intercepting proxy. The app follows the steered path and ships the validation body, session cookie, and PLU traffic to non-POS infrastructure.
- **Remediation:** Set `AllowAutoRedirect = false`; set `UseProxy = false` (or pin a known proxy); after each send, verify the final `RequestUri` still matches the configured Sapphire origin and refuse host drift.

> **Note on correlation:** Findings 1 and 2 are not independent. The redirect/proxy steering in #2 is only practically exploitable *because* #1 removes certificate validation. Fixing #1 properly (pinning) largely collapses #2 — but setting `AllowAutoRedirect = false` / `UseProxy = false` remains cheap defense-in-depth.

---

## Pass 2 — Response Trust & Robustness (the subtler findings)

These were found on a second, deeper pass explicitly directed away from the two obvious findings above. The unifying theme: **the app trusts response content that, given Findings 1–2, is attacker-controllable.**

### Finding 3 — Unbounded response buffering + greedy fetch = trivial DoS — **High**

- **Location:** `src/Core/SapphireHttpUtil.cs:25`, `src/Core/SapphireClient.cs:38`
- **Description:** Every response is read whole via `ReadAsStringAsync()` (no `HttpCompletionOption.ResponseHeadersRead`, no size cap) and then `XDocument.Parse`'d into a DOM (allocating again). PLU fetch requests `const int PageSize = 1_000_000`, and login immediately triggers a full cache refresh (PLUs, departments, tax rates, age validations).
- **Exploit scenario:** With MITM assumed, the attacker returns a huge or pathological XML body. The app buffers it fully, then re-allocates during parse, causing memory exhaustion / long GC pauses / process termination — all before any validation runs.
- **Remediation:** Use `HttpCompletionOption.ResponseHeadersRead`, enforce a maximum body size before buffering, and parse from a bounded stream with hardened `XmlReaderSettings` quotas (`MaxCharactersFromEntities`, `MaxCharactersInDocument`, `DtdProcessing = Prohibit`). Paginate `vPLUs` rather than requesting one million items at once.

### Finding 4 — Fault detection is fragile, case-sensitive substring matching (fails open) — **Medium**

- **Location:** `src/Core/SapphireHttpUtil.cs:47-50`
- **Description:** A response is treated as a failure **only** if the HTTP status is non-success **or** the body contains the exact-case substrings `"VFI:Fault"`, `"faultCode"`, or `"faultString"`. Anything else — wrong casing, different namespace, unrelated XML/HTML — is treated as success and handed to the data/cache layer.
- **Exploit scenario:** A MITM returns HTTP 200 with a fault payload using different casing/namespacing, or unrelated content omitting those substrings. The client accepts it as valid, refreshes caches with empty/partial objects, and the UI proceeds as if the device returned legitimate state.
- **Remediation:** Validate the response **positively**: expected root element, expected namespace, required children, explicit fault-schema handling. Unexpected XML should fail closed.

### Finding 5 — Malformed response fields silently coerced to defaults (state poisoning + write-back) — **Medium**

- **Location:** `src/Core/Models/ModelConverter.cs:21-26, 206-226`
- **Description:** Network fields (UPC, department ID, product code, price, tax IDs, age-validation IDs) are parsed permissively. Invalid numerics fall back to `0`/`1` or are dropped, and the resulting objects are cached and later reused for backup and update operations.
- **Exploit scenario:** A MITM alters `<price>`, `<department>`, `<upc>`, or `sysid`. The app does not reject the record — it silently coerces to defaults, displays/caches the poisoned data, and the user can later export it or write it back via bulk/save flows.
- **Remediation:** Treat missing/malformed required fields as hard parse failures. Surface a validation error for the record/response rather than defaulting to operational values like `0`.

### Finding 6 — Response-supplied foreign keys dereferenced without null checks — **Low**

- **Location:** `src/DesktopApp/ViewModels/EditPageVm.cs:358-425`
- **Description:** The edit workflow assumes department/tax/age-validation IDs from the network always resolve, dereferencing `taxRate.Name`, `ageValidation.Name`, `department.Name` without null checks (and assuming `GetDepartmentByNameAsync()` returned non-null).
- **Exploit scenario:** A MITM injects a PLU referencing nonexistent IDs; loading/saving it throws `NullReferenceException` and breaks the session.
- **Remediation:** Validate foreign keys before dereference; fail the operation with a clear error if any referenced object is missing.

### Finding 7 — Raw attacker-controlled response text written to local logs and surfaced in UI — **Low**

- **Location:** `src/Core/SapphireHttpUtil.cs:52`, `src/DesktopApp/ViewModels/AccountPageVm.cs:132`
- **Description:** On failure the full `responseContent` is logged (desktop logs go to `%LocalAppData%`). The login path also throws `SapphireRequestException(responseContent)` whose `.Message` is bound directly into the UI (`this.LoginError = ex.Message`).
- **Exploit scenario:** A MITM returns a crafted failure body with misleading instructions, control characters, or reflected values. The app persists it to log files and renders it as trusted-looking error text.
- **Remediation:** Log structured error metadata, not full bodies, by default. Truncate/sanitize server-provided text. Show generic user-facing errors; keep raw details behind an opt-in diagnostic path.

### Finding 8 — Console tool prints full PLU inventory to stdout — **Info**

- **Location:** `src/Console/Program.cs:51, 56`
- **Description:** The console app logs every PLU UPC and description received. On a shared terminal, CI runner, or with output redirection, inventory data is exposed outside the app boundary. Local disclosure, not remote exfiltration.
- **Remediation:** Make verbose item logging opt-in, or redact by default.

---

## Categories Reviewed and Found Clean

- **Exfiltration / C2 channels:** No telemetry, analytics, crash reporting, SMTP, DNS callbacks, webhooks, raw sockets, secondary `HttpClient`, update-checker, remote script/assembly loading, or hardcoded external domains anywhere in `./src`. (Confirmed in both passes.)
- **XXE / external-entity parsing:** Only `XDocument.Parse(responseContent)` is used (no `XmlDocument`, custom `XmlReader`, or explicit `XmlResolver`). On `net6.0`, DTD processing is off by default, so XXE could **not** be confirmed as a real finding from source. The genuine XML risk is the missing size/quota limits (Finding 3), not entity expansion. *(Hardening the parser settings per Finding 3's remediation also closes any residual doubt.)*
- **Credential persistence:** No password or cookie is written to disk. `settings.json` stores only `Hostname`, `Username`, and `UseMocks` in local app storage. Cookie and password live only in memory.
- **Path traversal / network-derived file paths:** None. Backup filenames are timestamp-based and rooted under app local storage.
- **Bulk import parser:** No file-import path exists (export/backup only) — no file-injection / billion-laughs surface there.
- **Outbound request injection (PLU bodies):** PLU/XML update bodies are built with `XElement`/`XAttribute`, which escapes content correctly. `cmd=` verbs in `SapphireClient` are hardcoded constants — no user-controlled command-header injection.
- **Hardcoded local IP:** `192.168.31.11` (console `Program.cs`, `Settings.cs`, `MockCredentialProvider.cs`) is an RFC1918 default/sample, not a leaked secret.
- **launchSettings.json / test artifacts:** No real hosts, passwords, or cookies baked in.

### One residual outbound-injection note (Low, local-access)

- **Location:** `src/Core/SapphireCredentialProvider.cs:71`
- The login line is built by raw concatenation — `cmd=validate&user={username}&passwd={password}` — with no URL-encoding. A username/password containing `&`, `\r`, or `\n` can inject extra command parameters into the outbound request. Requires local UI access, so Low. Restrict the allowed character set and reject CR/LF/separators before insertion. (PLU update bodies are not affected — they use `XElement`.)

---

## Recommended Remediation Order

1. **Finding 1** — Pin the POS certificate / public key (stop trusting any cert). Highest leverage; contained to `HttpClientHttpRequestSender.cs`.
2. **Finding 2** — `AllowAutoRedirect = false`, `UseProxy = false`, post-send host-match check (same file).
3. **Findings 3 + 4** — Cap response size + `ResponseHeadersRead`; make fault detection validate expected root/namespace and fail closed. Both contained to `SapphireHttpUtil.cs` (plus the `PageSize` constant in `SapphireClient.cs`).
4. **Finding 5** — Hard-fail on malformed required response fields instead of defaulting (`ModelConverter.cs`).
5. **Findings 6, 7, 8** — Hardening: null-check response FKs, stop logging/binding raw response bodies, gate console inventory dump.

---

## Severity Summary

| # | Finding | Severity | Primary file |
|---|---------|----------|--------------|
| 1 | TLS certificate validation disabled | High | `HttpClientHttpRequestSender.cs:24` |
| 2 | Permissive handler defaults (redirect/proxy) | High | `HttpClientHttpRequestSender.cs:21` |
| 3 | Unbounded response buffering + greedy fetch (DoS) | High | `SapphireHttpUtil.cs:25`, `SapphireClient.cs:38` |
| 4 | Fail-open substring fault detection | Medium | `SapphireHttpUtil.cs:47-50` |
| 5 | Malformed response fields coerced to defaults | Medium | `ModelConverter.cs:21-26,206-226` |
| 6 | Response FKs dereferenced without null checks | Low | `EditPageVm.cs:358-425` |
| 7 | Raw response text logged + bound to UI | Low | `SapphireHttpUtil.cs:52`, `AccountPageVm.cs:132` |
| 8 | Console prints full inventory to stdout | Info | `Console/Program.cs:51,56` |
| — | Login line raw-concatenation injection | Low | `SapphireCredentialProvider.cs:71` |
