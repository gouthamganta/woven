# Google OAuth Login Debugging Journey

## Problem Statement
Google OAuth login at www.wooven.me fails with 404 error when POSTing to `/auth/google`.

**Initial Error:**
```
POST https://www.wooven.me/auth/google
Status: 404 Not Found
```

---

## Root Causes Identified

### 1. HTTP vs HTTPS for Backend Communication
**Problem:** nginx was configured to proxy with `http://`, but Azure Container Apps backend requires `https://`

**Evidence:**
- Backend responds with `301 Redirect` when accessed via HTTP
- Backend successfully responds with `{"status":"alive"}` when accessed via HTTPS

**Fix Applied:** Changed all `proxy_pass` directives from `http://` to `https://`
- Commit: `af97beb`
- Result: Changed error from **404** to **502 Bad Gateway**

---

### 2. SSL Certificate Verification
**Problem:** nginx couldn't verify backend's internal SSL certificate

**Error Log:**
```
peer closed connection in SSL handshake (104: Connection reset by peer)
while SSL handshaking to upstream
```

**Fix Attempted:** Added `proxy_ssl_verify off;` to all proxy locations
- Commit: `6fba76e`
- Result: **Still 502 - SSL handshake still failing**

---

### 3. SSL SNI Configuration
**Problem:** nginx wasn't sending proper Server Name Indication during SSL handshake

**Fix Attempted:** Added SNI configuration:
```nginx
proxy_ssl_server_name on;
proxy_ssl_name woven-prod-backend.internal.victoriousstone-7adf719a.centralindia.azurecontainerapps.io;
```
- Commit: `fbfc4e7`
- Result: **Backend became unavailable** (separate issue)

---

## Architecture & Configuration

### Current Setup
```
Browser (www.wooven.me)
    ↓ HTTPS
Frontend Container (nginx:alpine)
    ↓ nginx reverse proxy
    ↓ SHOULD proxy to...
Backend Container (internal)
    ↓ HTTP 8080
    /auth/google endpoint
```

### Backend Configuration
- **Type:** Internal-only (`external_enabled = false` in Terraform)
- **FQDN (Internal):** `woven-prod-backend.internal.victoriousstone-7adf719a.centralindia.azurecontainerapps.io`
- **Protocol:** Requires HTTPS (redirects HTTP with 301)
- **Certificate:** Self-signed/internal certificate that nginx can't verify

### Frontend nginx Configuration
**File:** `frontend/woven-frontend/nginx.conf.static`

**Key Directives Tested:**
```nginx
server {
    listen 80;

    # DNS resolver for Azure internal DNS
    resolver 168.63.129.16 valid=30s;

    location = /auth/google {
        proxy_pass https://woven-prod-backend.internal..../auth/google;

        # SSL configuration attempts:
        proxy_ssl_verify off;                     # Disable cert verification
        proxy_ssl_server_name on;                  # Enable SNI
        proxy_ssl_name woven-prod-backend.internal...;  # Set SNI name

        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## All Attempts & Results

### Attempt 1: Fix BACKEND_URL Protocol (Terraform)
**Change:** `http://${backend_fqdn}` → `https://${backend_fqdn}`
**File:** `infra/modules/container_apps/main.tf`
**Result:** ❌ Didn't help - using static nginx config

---

### Attempt 2: Make Backend External (CLI)
**Command:** `az containerapp ingress update --type external`
**Reason:** Bypass nginx proxy issues
**Result:** ❌ Backend became unavailable/broken when external

---

### Attempt 3: Revert Backend to Internal (CLI)
**Command:** `az containerapp ingress update --type internal`
**Result:** ✅ Backend working again (internal)

---

### Attempt 4: Change nginx to HTTPS
**Change:** All `proxy_pass http://...` → `proxy_pass https://...`
**Commit:** `af97beb`
**Result:** ⚠️ Progress! 404 → 502 (nginx now proxying)

---

### Attempt 5: Disable SSL Verification
**Change:** Added `proxy_ssl_verify off;` to all locations
**Commit:** `6fba76e`
**Result:** ❌ Still 502 - SSL handshake failing

---

### Attempt 6: Add SNI Configuration
**Change:** Added `proxy_ssl_server_name on;` and `proxy_ssl_name ...`
**Commit:** `fbfc4e7`
**Result:** ❌ Backend became unavailable (different issue)

---

## Current State (as of 2026-02-17 03:30 UTC)

### Frontend
- **Revision:** 0000021
- **Status:** Healthy
- **Config:** HTTPS proxy with SSL verify off + SNI

### Backend
- **Revision:** 0000021
- **Status:** Shows "Healthy" but returns "Container App Unavailable"
- **Ingress:** External (unintended - Terraform not applied)
- **Problem:** Backend is external in Azure, but Terraform code has internal

**Root Issue:** Deploy workflow updates containers but NOT infrastructure. Terraform changes were committed but never applied to Azure.

---

## Key Findings

### ✅ What Works
1. DNS resolution works (168.63.129.16 resolver)
2. Backend responds on HTTPS when accessed with curl `-k` (skip verify)
3. nginx config loads correctly (verified via exec)
4. Backend `/auth/google` endpoint exists in code

### ❌ What Doesn't Work
1. nginx → backend HTTPS connection fails with SSL handshake error
2. `proxy_ssl_verify off` alone insufficient
3. SNI configuration didn't resolve the issue
4. Backend breaks when made external

### 🤔 Observations
- Backend works fine when **internal** and accessed via **curl with -k**
- nginx with `proxy_ssl_verify off` should work like `curl -k` but doesn't
- SSL handshake fails even with verification disabled
- Backend certificate appears to be self-signed/internal

---

## Why nginx Proxy Fails

### Evidence from Testing
```bash
# This WORKS (from frontend container):
curl -k https://woven-prod-backend.internal.../health/live
# Returns: {"status":"alive"}

# This FAILS (nginx with proxy_ssl_verify off):
# Error: peer closed connection in SSL handshake
```

### Theory
nginx's SSL handshake is being rejected by the backend even with `proxy_ssl_verify off`. Possible reasons:
1. nginx SSL/TLS protocol version mismatch
2. Backend rejecting based on SNI or other headers
3. Azure Container Apps internal networking issue
4. Certificate chain issue that `proxy_ssl_verify off` doesn't bypass

---

## Alternative Approaches Not Tried

### 1. Use IP Address Instead of FQDN
```nginx
# Bypass DNS and SSL name verification
proxy_pass https://100.100.0.204:443/auth/google;
proxy_ssl_verify off;
```

### 2. HTTP Backend (Change Backend Config)
Make backend accept HTTP on internal network:
- Change backend to listen on HTTP only
- Remove HTTPS redirect for internal traffic

### 3. Different Proxy Method
- Use HTTP/2 proxy
- Try different nginx SSL directives (`proxy_ssl_protocols`, `proxy_ssl_ciphers`)

### 4. Service Mesh / API Gateway
- Azure Application Gateway
- Azure API Management
- Different routing architecture

---

## Commits History

| Commit | Description | Result |
|--------|-------------|--------|
| `af97beb` | Use HTTPS for backend proxy | 404 → 502 |
| `6fba76e` | Add proxy_ssl_verify off | Still 502 |
| `fbfc4e7` | Add SNI configuration | Backend unavailable |

---

## Deployment Workflow Issue

**Critical Discovery:** The GitHub Actions deploy workflow (`deploy.yml`) only deploys container images:
```yaml
- Build backend image (ACR)
- Build frontend image (ACR)
- Deploy backend container
- Deploy frontend container
```

**It does NOT run Terraform!**

The Terraform workflow (`terraform.yml`) runs separately and requires manual trigger for `apply`:
```yaml
workflow_dispatch:
  inputs:
    action: [plan, apply]
```

**Impact:** Infrastructure changes (like `external_enabled = false`) were committed but never applied to Azure.

---

## Recommendations

### Short-term Fix Options

#### Option A: Make Backend Accept HTTP Internally
**Pros:** Simplest, just change backend config
**Cons:** Less secure, requires backend code change

#### Option B: Fix nginx SSL Configuration
**Try:** Use backend IP instead of FQDN
**Pros:** May bypass SSL name issues
**Cons:** Hard-coded IP, less maintainable

#### Option C: Make Backend External (and fix the issues)
**Pros:** Bypasses nginx proxy entirely
**Cons:** Backend was broken when external (needs investigation)

### Long-term Solution

**Use Azure-managed SSL/TLS:**
1. Use Azure Application Gateway or API Management
2. Let Azure handle SSL termination
3. Backend communicates via HTTP internally

**Or: Fix Container Apps internal communication**
1. Investigate why backend is rejecting nginx SSL handshake
2. Check Azure Container Apps documentation for internal HTTPS
3. May need Azure support to diagnose

---

## Next Steps

1. **Immediate:** Run Terraform apply to sync infrastructure state
   ```bash
   cd infra
   terraform apply -var-file="environments/prod/terraform.tfvars"
   ```

2. **Test:** Try using backend IP address in nginx config

3. **Investigate:** Why backend breaks when made external

4. **Consider:** Alternative architecture (API Gateway, etc.)

---

## Lessons Learned

1. **Container Apps internal communication uses HTTPS** (not HTTP as initially assumed)
2. **Deploy workflow ≠ Terraform workflow** (infrastructure changes need separate apply)
3. **`proxy_ssl_verify off` isn't enough** for Azure Container Apps internal SSL
4. **nginx SSL handling differs from curl** even with same "skip verification" settings
5. **Making backend external causes different problems** (needs separate investigation)

---

## Useful Commands

### Check Backend Status
```bash
az containerapp show --name woven-prod-backend --resource-group woven-prod-rg \
  --query "{external:properties.configuration.ingress.external, running:properties.runningStatus}"
```

### Test Backend Directly
```bash
# From frontend container:
az containerapp exec --name woven-prod-frontend --resource-group woven-prod-rg \
  --command "curl -k -v https://woven-prod-backend.internal.../health/live"
```

### Check nginx Config
```bash
az containerapp exec --name woven-prod-frontend --resource-group woven-prod-rg \
  --command "cat /etc/nginx/nginx.conf"
```

### View Logs
```bash
# Frontend logs:
az containerapp logs show --name woven-prod-frontend --resource-group woven-prod-rg --tail 50

# Backend logs:
az containerapp logs show --name woven-prod-backend --resource-group woven-prod-rg --tail 50
```

---

## Contact & Support

If this issue persists, consider:
1. Azure Support ticket for Container Apps internal networking
2. nginx community/forums for SSL proxy configuration
3. Alternative architecture review

---

*Document created: 2026-02-17*
*Last updated: 2026-02-17 03:30 UTC*
*Session: Extended debugging of Google OAuth login failure*
