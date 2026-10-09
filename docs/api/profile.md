# Profile API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Profile endpoints manage user insights, opinions, and accessibility preferences. Insights surface behavioral patterns, and users can submit monthly feedback.

**Key Concepts:**
- **Insights** — AI-generated observations about user behavior
- **Opinion** — User feedback on their experience (1 per calendar month)
- **Accessibility** — Motion reduction, high contrast, pronouns

**Source:** [`backend/WovenBackend/Endpoints/MeEndpoints.cs`](../../backend/WovenBackend/Endpoints/MeEndpoints.cs)

---

## Endpoints

### GET /me/insights

**Description:** Fetch user insights and opinion prompt (if due).

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "insights": [
    "You tend to respond more to profiles with outdoor photos",
    "Your best conversations start with questions about values"
  ],
  "shouldAskOpinion": true,
  "opinionTrigger": "no_dates_yet",
  "opinionPrompt": "How's your experience been so far?"
}
```

**Fields:**
- `insights` — Array of behavioral observations
- `shouldAskOpinion` — True if opinion submission is due
- `opinionTrigger` — Reason for prompt (`no_dates_yet`, `pattern_shift`, `high_rejection`, `low_depth`)

**Source:** [`MeEndpoints.cs:29-56`](../../backend/WovenBackend/Endpoints/MeEndpoints.cs)

---

### POST /me/insights/opinion

**Description:** Submit user opinion/feedback (limited to 1 per calendar month).

**Authentication:** Required (JWT)

**Rate Limit:** 1 request per user per calendar month

**Request:**
```json
{
  "text": "I really appreciate the focus on conversation quality over endless swiping.",
  "trigger": "no_dates_yet"
}
```

**Validation:**
- `text` 1-300 characters
- `trigger` must be valid (see list below)

**Valid Triggers:**
- `no_dates_yet`
- `pattern_shift`
- `high_rejection`
- `low_depth`

**Response (200 OK):**
```json
{
  "submitted": true
}
```

**Errors:**
- `400 TEXT_REQUIRED` — Text is missing
- `400 TEXT_TOO_LONG` — Text exceeds 300 characters
- `400 INVALID_TRIGGER` — Unknown trigger
- `429` — Already submitted this month (see `Retry-After` header)

**Source:** [`MeEndpoints.cs:59-92`](../../backend/WovenBackend/Endpoints/MeEndpoints.cs)

---

### PUT /me/accessibility

**Description:** Update accessibility preferences and pronouns.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "reduceMotion": true,
  "highContrast": false,
  "displayPronouns": "she/her"
}
```

**Validation:**
- `displayPronouns` ≤ 50 characters

**Response (200 OK):**
```json
{
  "updated": true
}
```

**Errors:**
- `400 PRONOUNS_TOO_LONG`

**Source:** [`MeEndpoints.cs:95-119`](../../backend/WovenBackend/Endpoints/MeEndpoints.cs)

---

### GET /me/accessibility

**Description:** Fetch current accessibility settings.

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "reduceMotion": true,
  "highContrast": false,
  "displayPronouns": "she/her"
}
```

**Defaults:**
- `reduceMotion`: `false`
- `highContrast`: `false`
- `displayPronouns`: `null`

**Source:** [`MeEndpoints.cs:122-140`](../../backend/WovenBackend/Endpoints/MeEndpoints.cs)

---

## Insights Generation

**Triggers:**
- Weekly behavioral analysis (runs Sunday nights)
- Pattern detection thresholds

**Storage:**
```csharp
// UserInsights table
InsightsJson: string[] // Array of insight strings
```

**Example Insights:**
- "You tend to respond more to profiles with outdoor photos"
- "Your best conversations start with questions about values"
- "Profiles in tech resonate with you 40% more than average"

**Source:** [`IInsightService.cs`](../../backend/WovenBackend/Services/Insights/IInsightService.cs)

---

## Opinion Submission Rules

**Rate Limit:** 1 opinion per calendar month

**Retry-After Calculation:**
```csharp
var now = DateTime.UtcNow;
var nextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
http.Response.Headers["Retry-After"] = ((int)(nextMonth - now).TotalSeconds).ToString();
```

**Use Case:** Collect qualitative feedback to improve ECHO matchmaking and UX.

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/MeEndpoints.cs`
- `backend/WovenBackend/Services/Insights/IInsightService.cs`
