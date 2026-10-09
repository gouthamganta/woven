# Woven API Reference

**Version:** 1.0  
**Base URLs:**
- **Production:** `https://api.wooven.me`
- **Development:** `http://localhost:5135`

---

## Overview

The Woven API is a RESTful HTTP API built with ASP.NET Core 10 Minimal API architecture. All endpoints return JSON responses and follow consistent patterns for authentication, error handling, and rate limiting.

**Key Features:**
- JWT-based authentication (Bearer tokens + HttpOnly cookies)
- Correlation IDs for request tracing
- Rate limiting with 429 responses
- Structured error responses with correlation tracking
- Real-time features via SignalR (separate WebSocket endpoint)

---

## General Patterns

### HTTP Methods

- **GET** — Retrieve resources (read-only, no side effects)
- **POST** — Create resources or perform actions
- **PUT** — Update resources (full replacement)
- **DELETE** — Remove resources

### Response Codes

| Code | Meaning | Usage |
|------|---------|-------|
| 200 | OK | Successful request with response body |
| 201 | Created | Resource created successfully |
| 204 | No Content | Successful request with no response body |
| 400 | Bad Request | Invalid input or validation failure |
| 401 | Unauthorized | Missing or invalid authentication |
| 403 | Forbidden | Valid auth but insufficient permissions |
| 404 | Not Found | Resource does not exist |
| 422 | Unprocessable Entity | Domain validation failure |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Server Error | Unexpected server error |

### Common Headers

**Request Headers:**
```http
Authorization: Bearer <jwt_token>
X-Idempotency-Key: <unique_key>  # For critical mutations
Content-Type: application/json
```

**Response Headers:**
```http
X-Correlation-ID: <16_char_hex_id>  # Request tracking ID
Retry-After: <seconds>  # On 429 responses
```

---

## Authentication

All authenticated endpoints require a valid JWT token. See [authentication.md](authentication.md) for details.

**Quick Example:**
```bash
curl -H "Authorization: Bearer eyJhbGc..." https://api.wooven.me/moments
```

---

## Rate Limiting

Rate limits vary by endpoint. See [rate-limiting.md](rate-limiting.md) for limits and handling.

**429 Response Example:**
```json
{
  "error": "Rate limit exceeded",
  "retryAfter": 3600,
  "correlationId": "a1b2c3d4e5f67890"
}
```

---

## Error Handling

All errors return a consistent JSON structure with correlation IDs. See [error-handling.md](error-handling.md).

**Error Response Example:**
```json
{
  "error": "MATCH_NOT_FOUND",
  "message": "Match does not exist",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

---

## API Endpoints by Feature

### Authentication
- [Authentication](authentication.md) — Login, logout, token management

### Onboarding
- [Onboarding](onboarding.md) — User registration flow, profile setup

### Core Features
- [Moments](moments.md) — Daily discovery deck, match responses
- [Chats](chats.md) — Messages, voice notes, trial decisions
- [Matches](matches.md) — Match management, balloon mechanics
- [Commons](commons.md) — Content feed, tiles, orbits

### User Features
- [Profile](profile.md) — User profile, insights, accessibility
- [Games](games.md) — Know Me, Red/Green Flag games
- [Coaching](coaching.md) — Weekly coaching summaries

### Media & Infrastructure
- [Media](media.md) — Photo/video uploads, SAS tokens
- [Notifications](notifications.md) — Web push subscriptions

---

## Versioning

Currently v1 (no version prefix in URLs). Future versions will use path-based versioning (e.g., `/v2/moments`).

---

## Source Reference

**Endpoint Files:** `backend/WovenBackend/Endpoints/*.cs`  
**Infrastructure:** `backend/WovenBackend/Infrastructure/`  
**Authentication:** `backend/WovenBackend/Auth/`

See individual endpoint documentation for implementation details and line references.

---

**Last Updated:** 2026-10-07  
**Maintained By:** Woven Engineering Team
