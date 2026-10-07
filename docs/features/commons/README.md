# Commons — Anonymous Content Feed

**Path:** `docs/features/commons/README.md`  
**Last Updated:** 2026-08-17  
**Status:** Shipped (Phase 2)

---

## What is Commons?

Commons is Woven's anonymous user-generated content feed. It's a discovery surface where users post short-lived Tiles (text, photos, videos, voice notes) and browse curated content from other users. Every piece of content is **fully anonymous** — no names, no profile photos, just resonance signals.

The feed surface is designed around two principles:
1. **Behavioral alignment over stated preferences** — the algorithm scores tiles using multi-dimensional embeddings (pillar values, reception/expression/preference vectors, intent tags)
2. **Discovery with guardrails** — 70% resonant (high-similarity), 30% discovery bucket (CF affinity + recency)

Commons serves three platform goals:
- **Cold-start signal generation** — early users generate behavioral data through orbits and dwell before matching
- **Ambient engagement** — keeps users active between Moments sessions
- **Expression surface** — tiles become highlight-able profile content after expiration

---

## Core Mechanics

### 1. Tile Lifecycle

```
POST tile → Moderation (OpenAI API) → 48h live → Expires → Highlight-able
```

- All tiles expire after **48 hours** from creation
- Text tiles auto-approve; media tiles queue for moderation
- After expiration, tiles can be pinned to **9 highlight slots** on your profile
- Maximum **10 active tiles** per user at any time

### 2. Energy System

Users have a **100-tile daily view cap** (resets midnight UTC). Each tile opened in the drawer increments the counter. When depleted:
- Feed returns HTTP 429
- Frontend shows "You've seen it all today" state
- Counter stored in Redis (TTL = end of UTC day) + DB write-through

**Why:** Prevents infinite scroll addiction, encourages quality browsing.

### 3. Feed Ranking

Every feed request scores a pool of up to **600 eligible tiles** (3× over-fetch, pruned to 200 final) using:

**5-component similarity** (renormalized to available signals):
1. **Pillar scores** (0.28) — 8-dimensional values alignment (Lifestyle, Energy, Communication, Affection, Stability, Values, Curiosity, Emotional Rhythm)
2. **Reception embedding** (0.32) — what viewer dwells on vs this tile's content (behavioral taste)
3. **Expression embedding** (0.18) — what viewer posts vs what owner posts (creative wavelength)
4. **Preference embedding** (0.12) — viewer's ChatNote preferences vs tile content (stated attraction patterns)
5. **Intent tags** (0.10) — Jaccard similarity on intent tags (low weight to prevent demographic silos)

**Final score** = `sim × 0.60 + recency × 0.25 + cfScore × 0.15`

Then split into:
- **Resonant bucket** (≥0.65 similarity): sorted by combined score
- **Discovery bucket** (<0.65 similarity): sorted by CF affinity + recency (not recency-only)

Interleaved at **70/30 ratio** (resonant/discovery).

**Page size:** 20 tiles  
**Feed caching:** Redis 2 hours per session (keyed by `sessionId`)

### 4. Orbit Gravity

The **◈** interaction on a tile. Two relationship modes:

| Type | Condition | Effect |
|---|---|---|
| **Romantic** | Orbiter's preferences match owner (gender/age/distance) | Upserts `orbit_gravity` with exponential decay, writes CandidateSignal on mutual |
| **Social** | Outside preference bounds | Creates FriendBridge on mutual social orbit |

**Mutual detection:**
- Romantic: owner has orbited any of orbiter's tiles (romantic type)
- Social: owner has orbited any of orbiter's tiles (social type)

Mutuals trigger:
- Romantic: `CandidateSignal` boost (7-day TTL, both directions)
- Social: `FriendBridge` with status `pending_both`

**Rate limit:** 50 orbits per user per day

---

## Data Model

### `tiles` (Tile.cs)
```
id              uuid (PK)
user_id         int
content_type    varchar(20) — 'text' | 'photo' | 'video' | 'voice'
content_text    text?
media_url       varchar(2048)?
embedding       vector(1536)? — text-embedding-3-small (null until TileEmbeddingService runs)
voice_embedding vector(192)?  — SpeechBrain ECAPA-TDNN (Phase 3D)
created_at      timestamptz
expires_at      timestamptz  — always created_at + 48h
is_expired      bool
is_highlighted  bool         — true once pinned to any slot
is_moderated    bool         — false until ModerationService approves
```

### `tile_views`
```
user_id     int
tile_id     uuid
viewed_at   timestamptz
duration_ms int?        — null if drawer opened but not closed before session end
```

### `tile_orbits`
```
id                 uuid (PK)
orbiter_id         int
tile_id            uuid
tile_owner_id      int
relationship_type  varchar(10) — 'romantic' | 'social'
orbited_at         timestamptz
UNIQUE (orbiter_id, tile_id)
```

### `orbit_gravity`
```
user_id       int
candidate_id  int
score         float   — increments by 1.0 per orbit, decays exponentially (e^(-0.1 × days))
last_orbit_at timestamptz
updated_at    timestamptz
PRIMARY KEY (user_id, candidate_id)
```

### `user_energy_meter`
```
user_id      int
date_utc     date
tiles_viewed int
PRIMARY KEY (user_id, date_utc)
```

---

## API Overview

### Commons Feed
- `GET /commons?page=1&sessionId={uuid}` — fetch feed page
- `POST /commons/refresh` — invalidate cache
- `POST /commons/{tileId}/view` — record view + dwell duration

### Tile Management
- `POST /tiles` — create tile
- `GET /tiles/mine` — list my tiles
- `POST /tiles/{tileId}/highlight` — pin to slot (1-9)
- `DELETE /tiles/{tileId}/highlight` — unpin
- `DELETE /tiles/{tileId}` — soft-delete (mark expired)

### Orbit
- `POST /orbit/{tileId}` — orbit a tile (◈)
- `GET /orbit/received` — orbits on my tiles
- `GET /orbit/bridges` — friend bridges I'm part of
- `POST /orbit/bridges/{bridgeId}/accept` — accept friend bridge
- `POST /orbit/bridges/{bridgeId}/decline` — decline friend bridge

See **[api.md](./api.md)** for full endpoint specs.

---

## Frontend Flow

1. User opens `/commons`
2. Component generates or restores `sessionId` from `sessionStorage`
3. Fetches page 1 via `CommonsService.getFeed(1, sessionId)`
4. Backend returns 20 tiles + sessionId (cached for 2h in Redis)
5. User scrolls → loads more pages (same sessionId = same cached pool)
6. User taps tile → drawer opens → `performance.now()` timestamp stored
7. User closes drawer → `recordView(tileId, durationMs)` called
8. If `durationMs >= 8000ms` AND tile owner ≠ viewer → `TileDwell` or `VoiceDwell` signal recorded
9. User taps **◎ Orbit** → `orbitTile(tileId)` → shows "◈ Orbiting this thought" or "A mutual pull — you both feel it"

**Refresh:**  
Tapping the refresh button clears `sessionStorage`, calls `POST /commons/refresh`, and reloads feed with a new sessionId.

---

## Workers & Background Jobs

### TileExpiryWorker
**Schedule:** Every 6 hours  
**What:** Marks tiles with `expires_at < now` as `is_expired = true`

### TileEmbeddingService
**Trigger:** Enqueued on tile creation (Service Bus / in-process Channel)  
**What:** Generates 1536-dim embedding via `text-embedding-3-small` and stores in `Tile.embedding`

### ModerationWorker
**Schedule:** Every 15 minutes (configurable)  
**What:** Pulls pending items from `moderation_queue`, calls OpenAI Moderation API (`omni-moderation-latest`), approves or rejects. Text tiles auto-approve inline; media tiles queue for worker.

---

## Design Rationale

### Why Anonymous?
**Cold-start problem.** Early users have no matches yet. Commons lets them express themselves, see others' content, and generate behavioral signals (orbits, dwell) that feed ECHO's collaborative filtering pipeline.

### Why 48-hour Expiration?
**Ephemerality drives engagement.** Short-lived content creates urgency and reduces analysis paralysis. After expiration, tiles become permanent profile highlights.

### Why 70/30 Resonant/Discovery Split?
**Balance familiarity with serendipity.** Pure resonant feeds become echo chambers. Pure discovery feels random. 70/30 keeps the feed aligned to user taste while surfacing behaviorally similar strangers (CF affinity).

### Why Energy Cap?
**Healthier UX.** Infinite scroll is addictive and low-value. 100 tiles/day encourages intentional browsing. Resets at UTC midnight (global constraint, simpler than per-user timezone).

### Why Orbit Gravity Decay?
**Recency matters.** A 90-day-old orbit is stale signal. Exponential decay (`e^(-0.1 × days)`) down-weights old orbits, preventing dead signals from polluting the candidate pool.

---

## Next Steps

See [CLAUDE.md Product Gaps](../../CLAUDE.md#product-gaps-updated-2026-06-04):
- **PreferenceEmbedding from ChatNotes** — worker stub exists, not wired
- **Voice embedding matching** — VoiceEmbeddingService exists, not used in feed ranking yet

---

## Related Docs

- **[tiles.md](./tiles.md)** — Tile entity, creation flow, expiration, highlights
- **[orbit.md](./orbit.md)** — Orbit gravity mechanics, relationship detection, mutual logic
- **[feed-algorithm.md](./feed-algorithm.md)** — Feed scoring, bucketing, interleaving
- **[moderation.md](./moderation.md)** — OpenAI moderation, auto-approval, queue
- **[frontend.md](./frontend.md)** — Angular component, state management, drawer UX
- **[backend.md](./backend.md)** — Services, endpoints, entity relationships
- **[api.md](./api.md)** — Full endpoint specs with request/response examples
