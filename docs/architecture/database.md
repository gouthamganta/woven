# Database Architecture

**Database:** PostgreSQL 16  
**ORM:** Entity Framework Core 10  
**Extensions:** pgvector 0.3.2  
**Hosting:** Azure Database for PostgreSQL Flexible Server (B_Standard_B1ms)  
**Port:** 5433 (local), 5432 (Azure)

---

## Overview

Woven's database is a single PostgreSQL instance managing all application data — users, matches, chats, games, tiles, analytics, and ECHO learning signals.

**Design principles:**
- **Single database** — no microservices, no database-per-feature
- **EF Core migrations** — all schema changes version-controlled
- **pgvector for embeddings** — 1536-dim OpenAI embeddings + 8-dim AI Profile
- **Encrypted PII** — AES-256-GCM at application level (EF Core value converters)
- **Indexes for performance** — composite indexes on frequent query patterns
- **Soft deletes where needed** — some entities keep `DeletedAt` instead of hard delete

---

## Schema Organization

Tables grouped by domain:

### 1. Users & Authentication

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **users** | Core user profiles | `id`, `email`, `name`, `profile_status` |
| **auth_identities** | OAuth identities (Google) | `provider`, `provider_subject`, `user_id` |
| **user_profiles** | Extended profile data | `user_id`, `bio`, `occupation`, `education` |
| **user_preferences** | Match preferences (age, distance, gender) | `user_id`, `age_min`, `age_max`, `gender_seeking` |
| **user_photos** | User photos | `user_id`, `url`, `sort_order` |
| **user_intents** | User dating goals | `user_id`, `intent` (SHORT_TERM, LONG_TERM, etc.) |
| **user_foundational_v1** | Foundational answers (8 pillars) | `user_id`, 8 pillar fields |
| **user_foundational_question_sets** | Dynamic foundational Q&A | `user_id`, `questions_json`, `answers_json` |
| **user_optional_fields** | Optional profile fields (horoscope, etc.) | `user_id`, `key`, `value`, `visibility` |
| **user_weekly_vibes** | Weekly vibe updates | `user_id`, `vibe_text`, `expires_at` |
| **user_dynamic_intake_sets** | Dynamic intake cycles | `user_id`, `cycle_id`, `questions_json` |

**Key indexes:**
- `users.email` — unique
- `auth_identities.(provider, provider_subject)` — unique
- `user_profiles.user_id` — unique (1:1)
- `user_photos.(user_id, sort_order)` — ordered retrieval

**Encrypted fields (AES-256-GCM):**
- `users.email` — PII protection
- `users.phone_number` — if/when added
- `user_profiles.full_name` — if/when added

---

### 2. Moments (Matching)

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **matches** | Match records (balloon state, trial) | `id`, `user_a_id`, `user_b_id`, `edge_owner_id`, `balloon_state`, `trial_ends_at` |
| **pending_matches** | Temporary matches before balloon pop | `id`, `user_id`, `candidate_id` |
| **daily_interactions** | Daily swipe allowances | `user_id`, `date_utc`, `count` |
| **moment_responses** | Swipe responses (MAGICAL/LOGICAL/SKIP) | `user_id`, `candidate_id`, `choice`, `created_at` |
| **blocks** | User blocks | `blocker_id`, `blocked_id`, `reason` |
| **chat_notes** | Background notes (never shown to users) | `match_id`, `author_id`, `content` |
| **chat_note_love_reactions** | Love reactions on notes | `chat_note_id`, `user_id` |
| **user_ratings** | Community ratings (platform-only) | `rated_user_id`, `rater_id`, `score` |

**Key indexes:**
- `matches.(user_a_id, user_b_id)` — unique
- `matches.balloon_expires_at` — worker scans for expiry
- `blocks.(blocker_id, blocked_id)` — unique
- `moment_responses.(user_id, candidate_id)` — prevents duplicate swipes

**Balloon state machine:**
- `ACTIVE` → balloon window open (72h)
- `CLOSED` → popped or expired (immutable)

**Edge matches:**  
When users make different choices (◈ vs ◇), `edge_owner_id` points to the user who got their choice.

---

### 3. Chats

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **chat_threads** | Chat threads between matches | `id`, `match_id`, `created_at` |
| **chat_messages** | Messages (text, voice, game invites) | `id`, `thread_id`, `sender_id`, `content`, `message_type` |
| **message_love_reactions** | Love reactions on messages | `message_id`, `user_id` |

**Message types:**
- `TEXT` — plain text
- `VOICE` — voice note (`meta_json` contains `{ audioUrl, durationSecs }`)
- `GAME_INVITE` — game invite (`meta_json` contains `{ gameType, sessionId }`)

**Key indexes:**
- `chat_threads.match_id` — unique (1:1)
- `chat_messages.thread_id` — ordered retrieval
- `chat_messages.created_at` — chronological sort

**Voice notes:**  
Stored in Azure Blob, URL in `meta_json`. Listen tracking via separate endpoint (`POST /chats/{threadId}/messages/{messageId}/voice-listened`).

---

### 4. ECHO (Matchmaking AI)

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **daily_decks** | Daily deck snapshots | `user_id`, `generated_at`, `items_json` |
| **daily_deck_items** | Normalized deck items | `deck_id`, `candidate_id`, `bucket`, `score` |
| **user_vectors** | User embedding vectors (1536-dim) | `user_id`, `embedding` (pgvector), `version` |
| **user_vector_tags** | Vector metadata | `vector_id`, `tag_type`, `tag_value` |
| **match_explanations** | AI-generated match explanations | `match_id`, `explanation_text`, `bridge_question` |
| **match_signal_logs** | Raw behavioral signals | `match_id`, `event_type`, `value`, `occurred_at` |
| **connection_scores** | Aggregated match signals | `match_id`, `score`, `last_updated_at` |
| **match_outcomes** | Match outcome labels (training data) | `match_id`, `outcome` (DATE, NO_DATE, etc.) |
| **candidate_exposures** | Deck exposure tracking | `user_id`, `candidate_id`, `shown_at` |
| **candidate_signals** | Swipe signals for learning | `user_id`, `candidate_id`, `signal_type`, `value` |
| **user_matching_weights** | Per-user learned weights | `user_id`, `weights_json` |
| **lin_ucb_user_models** | LinUCB bandit models | `user_id`, `theta`, `a_inv` |
| **user_behavioral_fingerprints** | 16-dim behavioral embeddings | `user_id`, `fingerprint` (pgvector) |
| **cf_scores** | Collaborative filtering scores | `user_a_id`, `user_b_id`, `score` |

**Key indexes:**
- `daily_decks.(user_id, generated_at)` — one deck per day
- `daily_deck_items.(deck_id, candidate_id)` — unique
- `daily_deck_items.(candidate_id, shown_at)` — time-based lookups
- `daily_deck_items.(bucket, score)` — bucket-based selection
- `user_vectors.user_id` — unique (latest version)
- `match_signal_logs.(match_id, occurred_at)` — chronological retrieval
- `connection_scores.match_id` — unique
- `cf_scores.(user_a_id, user_b_id)` — unique

**pgvector indexes:**
- `user_vectors.embedding` — HNSW index for cosine similarity search
- `user_behavioral_fingerprints.fingerprint` — HNSW index

**ECHO signal types:**
- `TimeToFirstMessageMs` — speed of first message
- `ChatDepthMessages` — total message count
- `TrialContinued` / `TrialEndedNoSpark` — trial decision
- `VoiceNoteListenComplete` — voice note played to end
- `MutualVoiceExchange` — both users sent voice notes
- `DateIdeaAccepted` — which date idea chosen
- `UserFlagged` — safety flag (never in scoring)

---

### 5. Games

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **game_sessions** | Game sessions (KnowMe, RedGreenFlag) | `id`, `match_id`, `game_type`, `status` |
| **game_rounds** | Rounds within sessions | `id`, `session_id`, `round_number`, `question_text` |
| **game_results** | User answers per round | `round_id`, `user_id`, `answer`, `is_correct` |
| **game_analytics** | Game completion analytics | `session_id`, `duration_ms`, `alignment_score` |
| **game_outcomes** | Final game outcomes | `session_id`, `outcome` (BOTH_COMPLETED, etc.) |

**Game types:**
- `KNOW_ME` — 10 questions, guess partner's answers
- `RED_GREEN_FLAG` — mutual flag voting

**Key indexes:**
- `game_sessions.match_id` — find games for a match
- `game_rounds.session_id` — ordered retrieval
- `game_results.(round_id, user_id)` — unique

---

### 6. Commons (Content Feed)

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **tiles** | User content posts | `id`, `author_id`, `content`, `media_url`, `expires_at` |
| **highlights** | Highlighted tiles (featured) | `tile_id`, `expires_at` |
| **tile_views** | Tile view tracking | `tile_id`, `viewer_id`, `dwell_time_ms` |
| **tile_orbits** | Orbit interactions (◈ on tiles) | `tile_id`, `user_id`, `created_at` |
| **tile_engagements** | Engagement metrics | `tile_id`, `views`, `orbits`, `comments` |
| **orbit_gravities** | Orbit strength scores | `tile_id`, `user_id`, `gravity_score` |
| **user_energy_meters** | User engagement energy | `user_id`, `energy_level`, `last_reset_at` |

**Key indexes:**
- `tiles.author_id` — user's own tiles
- `tiles.expires_at` — expiry worker
- `tile_orbits.(tile_id, user_id)` — unique
- `tile_views.(tile_id, viewer_id)` — unique

**Orbit gravity:**  
Score based on dwell time, view count, and recency. Used for SharedTileAffinity matchmaking component.

---

### 7. Moderation & Trust

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **moderation_queue** | Content moderation queue | `id`, `content_type`, `content_id`, `status` |
| **tile_reports** | User reports on tiles | `tile_id`, `reporter_id`, `reason` |
| **security_audit_logs** | Security event logs | `user_id`, `event_type`, `ip_address`, `created_at` |
| **user_verifications** | Identity verification records | `user_id`, `verification_type`, `status` |
| **reference_photo_embeddings** | Catfish detection (photo consistency) | `user_id`, `embedding` (pgvector) |

**Moderation statuses:**
- `PENDING` — awaiting review
- `APPROVED` — passed moderation
- `REJECTED` — blocked

**Key indexes:**
- `moderation_queue.status` — worker scans pending items
- `tile_reports.(tile_id, reporter_id)` — unique

---

### 8. Analytics & Insights

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **analytics_events** | User event tracking | `user_id`, `event_type`, `properties_json`, `created_at` |
| **ab_experiments** | A/B test definitions | `id`, `name`, `variants_json` |
| **ab_assignments** | User A/B assignments | `user_id`, `experiment_id`, `variant` |
| **ab_conversions** | A/B conversion tracking | `assignment_id`, `converted_at` |
| **user_insights** | User-specific insights | `user_id`, `insight_type`, `insight_json` |
| **user_interaction_logs** | Interaction logs for ECHO | `user_id`, `interaction_type`, `target_id`, `created_at` |

**Key indexes:**
- `analytics_events.(user_id, created_at)` — time-range queries
- `ab_assignments.(user_id, experiment_id)` — unique

---

### 9. Coaching & Feedback

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **coaching_summaries** | Weekly coaching summaries | `user_id`, `summary_text`, `generated_at` |
| **date_feedbacks** | Post-date feedback | `match_id`, `responder_id`, `rating`, `went_on_date` |
| **date_feedback_prompts** | Feedback question templates | `id`, `prompt_text`, `category` |
| **chat_availability_signals** | Pre-date availability hints | `match_id`, `user_id`, `availability_json` |

**Key indexes:**
- `coaching_summaries.(user_id, generated_at)` — latest summary retrieval
- `date_feedbacks.match_id` — unique per match

---

### 10. Seasons & Venues

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **seasons** | Seasonal events/themes | `id`, `name`, `starts_at`, `ends_at` |
| **user_season_responses** | User responses to seasonal prompts | `season_id`, `user_id`, `response_text` |
| **(Future: Venues)** | Date venue recommendations | TBD |

---

### 11. Utility Tables

| Table | Purpose | Key Fields |
|-------|---------|------------|
| **spark_wallets** | User spark balances | `user_id`, `balance`, `updated_at` |
| **user_push_subscriptions** | Web Push subscriptions | `user_id`, `endpoint`, `p256dh`, `auth` |
| **idempotency_records** | Idempotency key tracking (24h TTL) | `user_id`, `key`, `response_json`, `created_at` |

**Key indexes:**
- `idempotency_records.(user_id, key)` — unique

---

## pgvector Extension

**Version:** 0.3.2  
**Embedding dimensions:**
- User text embeddings: 1536-dim (OpenAI `text-embedding-3-small`)
- AI Profile embeddings: 8-dim (custom, one per pillar)
- Behavioral fingerprints: 16-dim (no OpenAI)

**Vector columns:**

| Table | Column | Dimension | Purpose |
|-------|--------|-----------|---------|
| `user_vectors` | `embedding` | 1536 | Text semantic search |
| `user_behavioral_fingerprints` | `fingerprint` | 16 | Behavioral similarity |
| `photo_embeddings` | `embedding` | 512 | Photo similarity (CLIP) |
| `reference_photo_embeddings` | `embedding` | 512 | Catfish detection |

**Indexes:**
```sql
CREATE INDEX idx_user_vectors_embedding ON user_vectors 
  USING hnsw (embedding vector_cosine_ops);

CREATE INDEX idx_behavioral_fingerprints ON user_behavioral_fingerprints 
  USING hnsw (fingerprint vector_cosine_ops);
```

**HNSW parameters:**
- `m = 16` — number of connections per layer
- `ef_construction = 64` — build-time search depth

**Similarity queries:**
```sql
SELECT user_id, 1 - (embedding <=> $target_vector) AS similarity
FROM user_vectors
ORDER BY embedding <=> $target_vector
LIMIT 20;
```

---

## Encrypted Fields

**Encryption:** AES-256-GCM at application level (EF Core value converters)

**Service:** [EncryptionService](../../backend/WovenBackend/Services/Security/EncryptionService.cs)

**Converters:**
- [EncryptedStringConverter](../../backend/WovenBackend/data/Converters/EncryptedStringConverter.cs) — strings
- [EncryptedBytesConverter](../../backend/WovenBackend/data/Converters/EncryptedBytesConverter.cs) — binary

**Encrypted columns:**
- `users.email` — PII
- (Future: `users.phone_number`, `user_profiles.full_name`)

**Key rotation:**  
Encryption keys stored in Azure Key Vault. Rotation strategy TBD (decrypt-all → re-encrypt pattern).

---

## Connection Pooling

**Npgsql configuration** (in [Program.cs](../../backend/WovenBackend/Program.cs)):

```csharp
npgsqlDataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = 50;
npgsqlDataSourceBuilder.ConnectionStringBuilder.MinPoolSize = 2;
npgsqlDataSourceBuilder.ConnectionStringBuilder.ConnectionIdleLifetime = 300;
```

**Why:**
- 14+ background workers + real-time API pods
- PostgreSQL default `max_connections = 100`
- Prevents pool saturation (stay under 100 with room for admin/migrations)

**Retry policy:**
```csharp
npgsqlOptions.EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(10),
    errorCodesToAdd: null);
```

---

## Migrations

**Strategy:** EF Core Code-First migrations

**Migration folder:** [backend/WovenBackend/Migrations/](../../backend/WovenBackend/Migrations/)

**Recent migrations:**
- `20260603000002_AddBridgeQuestionToMatchExplanation` — added `bridge_question` to MatchExplanation
- `20260604000001_AddCoachingSummaries` — added CoachingSummary table
- `20260604200154_FixMatchSignalLogOccurredAt` — fixed DateTime → DateTimeOffset
- `20260604201504_AddDailyDeckItems` — normalized DailyDeckItems table

**Creating migrations:**
```bash
cd backend/WovenBackend
dotnet ef migrations add MigrationName
dotnet ef database update
```

**Production deployment:**  
Migrations run automatically on app startup (configured in [Program.cs](../../backend/WovenBackend/Program.cs)).

---

## Indexes Strategy

**Composite indexes for common queries:**

```sql
-- Daily deck retrieval (one per day per user)
CREATE INDEX idx_daily_decks_user_date ON daily_decks (user_id, generated_at DESC);

-- Deck item selection (bucket-based)
CREATE INDEX idx_daily_deck_items_bucket_score ON daily_deck_items (bucket, score DESC);

-- Match signal logs (chronological per match)
CREATE INDEX idx_match_signal_logs_match_time ON match_signal_logs (match_id, occurred_at DESC);

-- Chat messages (chronological per thread)
CREATE INDEX idx_chat_messages_thread_time ON chat_messages (thread_id, created_at DESC);

-- Balloon expiry worker (scans for expired balloons)
CREATE INDEX idx_matches_balloon_expires ON matches (balloon_expires_at) WHERE balloon_state = 'ACTIVE';
```

**Partial indexes:**  
Used for filtered queries (e.g., only scan `ACTIVE` balloons, not `CLOSED`).

---

## Soft Deletes

**Pattern:** Some entities keep `deleted_at` instead of hard delete.

**Entities with soft deletes:**
- `Tile` — soft delete to preserve Orbit history
- (Future: `Match`, `ChatMessage` for audit trails)

**Query pattern:**
```csharp
var activeTiles = db.Tiles.Where(t => t.DeletedAt == null);
```

---

## Database Size Estimates

**Current size (production):**  
~500 MB (early stage, < 1,000 users)

**Projected size (10,000 users):**
- Users + profiles: ~50 MB
- Matches + chats: ~200 MB
- Embeddings (1536-dim × 10k users): ~60 MB
- Analytics events: ~100 MB
- Total: ~500 MB → **5 GB**

**Scaling strategy:**
- **Vertical:** Upgrade from B_Standard_B1ms (1 vCore, 2GB RAM) to GP_Standard_D2s_v3 (2 vCore, 8GB RAM)
- **Horizontal:** Read replicas for analytics queries (future)
- **Archival:** Move old analytics events to cold storage (Azure Blob)

---

## Backup & Recovery

**Azure Postgres Flexible Server:**
- **Automated backups:** Daily (retained 7 days)
- **Point-in-time restore:** Up to 7 days back
- **Geo-redundant backups:** Not enabled (cost optimization)

**Manual backups (critical events):**
```bash
pg_dump -h <host> -U <user> -d woven_db -F c -f woven_db_backup.dump
```

---

## Known Issues

**Issue:** No read replicas yet  
**Impact:** Analytics queries block real-time traffic  
**Mitigation:** Run heavy analytics via batch workers at off-peak hours (2–5 AM UTC)

**Issue:** No connection pool monitoring  
**Impact:** Can't detect pool saturation before it happens  
**Fix:** Add Prometheus metrics for connection pool state

---

## Next Steps

1. **Add read replicas** — offload analytics queries
2. **Connection pool monitoring** — Prometheus + Grafana
3. **Query performance tuning** — EXPLAIN ANALYZE on slow queries
4. **Archival strategy** — move old events to Azure Blob
5. **Key rotation automation** — decrypt-all → re-encrypt pattern

---

**Last Updated:** 2026-10-07  
**Evidence:** [WovenDbContext.cs](../../backend/WovenBackend/data/WovenDbContext.cs), [Migrations/](../../backend/WovenBackend/Migrations/), [Entities/](../../backend/WovenBackend/data/Entities/)
