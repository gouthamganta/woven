# Moderation System

**Last Updated:** 2026-10-07  
**Status:** Production (disabled in dev)

---

## Overview

The moderation system in Woven provides multi-layered content safety through:

1. **AI moderation** (OpenAI Moderation API)
2. **User reporting** (tiles, messages, profiles)
3. **Trust scoring** (velocity, multi-account, bot detection)
4. **Catfish detection** (duplicate/stock photo matching)
5. **Block system** (user-initiated blocking)
6. **Manual review queue** (admin dashboard)

---

## Design Principles

### Invisible UX
No user-facing "AI moderation" branding. Safety is infrastructure, not a feature.

### Women-first safety
Non-negotiable. If a feature feels predatory, it gets killed.

### Behavioral signals > stated preferences
Trust scores derived from actions (velocity, multi-account), not self-reports.

### Dev convenience
`Moderation:IsModerationEnabled = false` auto-approves all content in local dev.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Content Created                         │
│                    (Tile, Message, Photo)                    │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
         ┌─────────────────────────┐
         │   ModerationService      │
         │   EnqueueAsync()         │
         └─────────────────────────┘
                       │
         ┌─────────────┴──────────────┐
         │                            │
         ▼                            ▼
   Text Content                 Media Content
   (auto-process)              (queued for worker)
         │                            │
         ▼                            ▼
   OpenAI API                  ModerationQueue
   (sync check)                (pending review)
         │                            │
         └──────────┬─────────────────┘
                    ▼
         ┌─────────────────────┐
         │  Decision:           │
         │  - approved          │
         │  - rejected          │
         │  - escalated         │
         └─────────────────────┘
```

### Components

| Component | Path | Purpose |
|-----------|------|---------|
| **ModerationService** | `Services/Moderation/` | Core moderation logic |
| **ModerationWorker** | `Services/Moderation/` | Background processing (5-min interval) |
| **TrustService** | `Services/Trust/` | Trust scoring & velocity checks |
| **CatfishDetectionService** | `Services/Trust/` | Duplicate/stock photo detection |
| **AdminEndpoints** | `Endpoints/AdminEndpoints.cs` | Review queue API |

---

## Database Schema

### ModerationQueue
```sql
CREATE TABLE moderation_queue (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tile_id      uuid NOT NULL REFERENCES tiles(id),
    user_id      int NOT NULL,
    queued_at    timestamptz NOT NULL DEFAULT now(),
    reviewed_at  timestamptz,
    reviewer_id  int,
    decision     varchar(20),      -- 'approved' | 'rejected'
    reject_reason varchar(200)
);

CREATE INDEX idx_moderation_queue_pending 
    ON moderation_queue (queued_at) 
    WHERE reviewed_at IS NULL;
```

### TileReports
```sql
CREATE TABLE tile_reports (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tile_id     uuid NOT NULL REFERENCES tiles(id),
    reporter_id int NOT NULL,
    reason      varchar(100) NOT NULL,
    reported_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_tile_reports_tile ON tile_reports (tile_id);
```

### Blocks
```sql
CREATE TABLE blocks (
    blocker_id int NOT NULL,
    blocked_id int NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (blocker_id, blocked_id)
);

CREATE INDEX idx_blocks_blocker ON blocks (blocker_id);
CREATE INDEX idx_blocks_blocked ON blocks (blocked_id);
```

---

## Configuration

**appsettings.json:**
```json
{
  "Moderation": {
    "IsModerationEnabled": false  // true in production
  },
  "OpenAI": {
    "ApiKey": "sk-..."  // from User Secrets / Key Vault
  }
}
```

**Behavior:**
- `IsModerationEnabled = false` → all content auto-approved
- `IsModerationEnabled = true` → text (sync check), media (worker queue)

---

## Moderation Flow

### Text Content (Tiles, Messages)
1. User submits text
2. `ModerationService.EnqueueAsync()` called
3. **Sync check** via OpenAI API
4. Flagged → tile expired, queue entry `decision = 'rejected'`
5. Clean → tile approved, `IsModerated = true`

### Media Content (Photos, Videos)
1. User uploads media
2. Tile created with `IsModerated = false`
3. `ModerationQueue` entry added
4. **ModerationWorker** (5-min interval) processes batch
5. OpenAI image moderation check
6. Decision stored, tile marked moderated/expired

### Profile Photos
1. Photo uploaded during onboarding
2. `ModerateImageAsync()` called
3. Returns: `APPROVED` | `ESCALATED` | `AUTO_REJECTED`
4. `AUTO_REJECTED` → upload blocked, user sees error
5. `ESCALATED` → allowed to proceed, flagged for manual review

---

## Trust Scoring

See [Trust System](../trust/README.md) for full details.

**Quick summary:**
- Default trust: `0.5`
- Threshold: `0.25` (below = restricted)
- Signals: velocity (10+ tiles/hour), multi-account (same fingerprint), bot detection (incomplete profile)

---

## Related Documentation

- [AI Moderation](./ai-moderation.md) — OpenAI API integration
- [User Reports](./user-reports.md) — Reporting system (not yet implemented)
- [Block System](./block-system.md) — User blocking mechanics
- [Prompt Injection](./prompt-injection.md) — LLM safety (future)
- [Review Queue](./review-queue.md) — Admin manual review
- [API Reference](./api.md) — Moderation endpoints

---

## Current Gaps

| Feature | Status | Priority |
|---------|--------|----------|
| User tile reporting | ❌ Not implemented | High |
| Message moderation | ❌ No checks | Medium |
| Voice note moderation | ❌ Not implemented | Medium |
| Prompt injection protection | ❌ Future | Low |
| Rate limiting on reports | ❌ Not implemented | Medium |

---

## Production Checklist

- [ ] Set `Moderation:IsModerationEnabled = true` in Azure
- [ ] Verify OpenAI API key in Key Vault
- [ ] Test ModerationWorker 5-min schedule
- [ ] Monitor `moderation_queue` for backlog
- [ ] Set up alerting for auto-rejected content spikes
- [ ] Train admin team on review queue UI
- [ ] Implement user tile reporting endpoint
- [ ] Add message moderation checks
