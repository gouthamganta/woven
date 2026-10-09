# Analytics Events Catalog

All tracked events in Woven's analytics system.

## Event Types

### User Interaction Logs

**Table:** `user_interaction_logs`  
**Service:** `IInteractionLogService`  
**Privacy:** Direct user_id FK (no hashing)

General product usage events. Used for feature adoption, UI interaction analysis, and product health monitoring.

```csharp
await _interactionLog.LogAsync(userId, eventType, context);
```

**Common event types:**
- Navigation events (page views, tab switches)
- Feature interactions (tile views, profile visits)
- UI actions (filter changes, sort toggles)
- Error states (API failures, validation errors)

**Context payload:**
- Flexible JSON object
- No strict schema enforcement
- Keep small (<1KB recommended)

---

### Analytics Events

**Table:** `analytics_events`  
**Service:** `IAnalyticsService`  
**Privacy:** Hashed user IDs, 12-month retention

Privacy-safe events for retention, cohort analysis, and A/B testing.

#### User Lifecycle

| Event | When Fired | Properties |
|-------|------------|------------|
| `user_registered` | User completes signup | `{ method, source }` |
| `onboarding_step_completed` | Each onboarding screen finished | `{ step, stepIndex, totalSteps }` |
| `onboarding_abandoned` | User exits onboarding before completion | `{ lastStep, progress }` |
| `profile_updated` | User edits profile fields | `{ fields }` |
| `account_deleted` | User deletes account | `{ reason }` |

#### Discovery

| Event | When Fired | Properties |
|-------|------------|------------|
| `moments_deck_viewed` | User opens Deck tab | `{ deckId, itemCount, tabName }` |
| `moment_responded` | User swaps on a Moment card | `{ momentId, action, sparkCost }` |
| `pending_saved` | User saves to Pending bucket | `{ candidateId }` |
| `pending_converted` | User acts on saved Pending | `{ candidateId, action }` |
| `commons_session_started` | User opens Commons feed | `{ }` |
| `tile_viewed` | Tile enters viewport >1s | `{ tileId, authorId, tileType }` |
| `tile_orbited` | User taps ◈ on a tile | `{ tileId, authorId, orbitCount }` |
| `tile_engaged` | User interacts with tile | `{ tileId, interactionType, dwellMs }` |
| `tile_posted` | User publishes a tile | `{ tileType, mediaCount }` |

#### Matching

| Event | When Fired | Properties |
|-------|------------|------------|
| `match_created` | Match created (pure or edge) | `{ matchId, matchType, edgeOwnerId? }` |
| `match_expired` | Balloon expires without pop | `{ matchId, hoursLived }` |
| `match_unmatched` | User ends match | `{ matchId, reason, messageCount }` |
| `balloon_timer_started` | Second user opens chat (trial starts) | `{ matchId, trialDurationMins }` |
| `find_love_unlocked` | User reaches final unlock stage | `{ matchId, daysSinceMatch }` |

#### Conversation

| Event | When Fired | Properties |
|-------|------------|------------|
| `chat_started` | First message sent in thread | `{ matchId, messageType }` |
| `message_sent` | Any message sent | `{ matchId, messageType, length }` |
| `game_invited` | User sends game invite | `{ matchId, gameType }` |
| `game_accepted` | Partner accepts game | `{ matchId, gameType }` |
| `game_rejected` | Partner declines game | `{ matchId, gameType }` |
| `game_completed` | Game reaches completion | `{ matchId, gameType, rounds }` |
| `nudge_shown` | Conversational nudge displayed | `{ matchId, nudgeType }` |
| `nudge_acted_on` | User responds to nudge | `{ matchId, nudgeType, responseType }` |
| `nudge_dismissed` | User dismisses nudge | `{ matchId, nudgeType }` |

#### Dating

| Event | When Fired | Properties |
|-------|------------|------------|
| `date_interest_expressed` | User signals readiness to meet | `{ matchId }` |
| `date_interest_mutual` | Both users signal readiness | `{ matchId }` |
| `venue_suggestions_viewed` | User opens venue picker | `{ matchId, venueCount }` |
| `availability_signal_sent` | User shares availability | `{ matchId, slots }` |
| `date_feedback_prompted` | System asks for date feedback | `{ matchId }` |
| `date_feedback_submitted` | User completes feedback form | `{ matchId, rating, wentWell }` |

#### Trust & Safety

| Event | When Fired | Properties |
|-------|------------|------------|
| `verification_started` | User begins ID verification | `{ method }` |
| `verification_completed` | Verification succeeds | `{ method, durationSecs }` |
| `verification_failed` | Verification fails | `{ method, reason }` |
| `catfish_flag_triggered` | System flags potential catfish | `{ userId, score }` |
| `content_moderated` | Content flagged by moderation | `{ contentType, contentId, reason }` |
| `report_submitted` | User submits safety report | `{ targetUserId, reason }` |
| `account_blocked` | User blocks another user | `{ blockedUserId, context }` |

#### Seasons & Vectors

| Event | When Fired | Properties |
|-------|------------|------------|
| `season_response_submitted` | User answers seasonal question | `{ seasonId, questionId }` |
| `weekly_pulse_submitted` | User completes weekly pulse | `{ weekId, responseCount }` |
| `insight_viewed` | User views coaching insight | `{ insightType }` |
| `opinion_submitted` | User submits opinion on topic | `{ topicId }` |
| `weight_learning_run` | ECHO weight learning completes | `{ matchesAnalyzed, weightsUpdated }` |

#### Engagement

| Event | When Fired | Properties |
|-------|------------|------------|
| `app_opened` | App launched or resumed | `{ source }` |
| `notification_received` | Push notification delivered | `{ notificationType }` |
| `notification_tapped` | User taps notification | `{ notificationType, deepLink }` |
| `notification_dismissed` | User dismisses notification | `{ notificationType }` |
| `weekly_digest_viewed` | User opens weekly digest | `{ weekId }` |
| `session_ended` | App backgrounded >5 mins | `{ durationSecs }` |

#### A/B Testing

| Event | When Fired | Properties |
|-------|------------|------------|
| `ab_experiment_assigned` | User assigned to experiment variant | `{ experimentId, variant }` |
| `ab_conversion` | User converts in experiment | `{ experimentId, variant, conversionType }` |

---

### Match Signal Logs

**Table:** `match_signal_logs`  
**Service:** `IMatchSignalService`  
**Privacy:** No PII (only user IDs), consumed by ECHO

Behavioral signals between matched users. Directional (viewer → candidate). Used for ECHO weight learning and preference extraction.

#### Core Signal Types

| EventType | EventValue | Metadata | Purpose |
|-----------|------------|----------|---------|
| `TileDwell` | dwell ms | `{ tileType }` | Tile engagement depth |
| `VoiceDwell` | dwell ms | — | Voice note listen time |
| `ProfileVisitDepth` | tiles viewed | — | Profile browsing depth |
| `BalloonPop` | 1.0 | — | User popped balloon |
| `TrialRequested` | 1.0 | — | User opened chat first |
| `TrialAccepted` | 1.0 | — | User chose CONTINUE |
| `TrialRejected` | 0.0 | — | User chose END (any reason) |
| `MessageSent` | 1.0 | `{ messageType }` | Message activity |
| `MessageResponseLatencyMs` | latency ms | — | Reply speed |
| `TimeToFirstMessageMs` | latency ms | — | Speed to first message (outcome proxy) |
| `SelfDisclosureRatio` | 0.0–1.0 | — | Share depth in messages |
| `GameCompleted` | rounds | `{ gameType }` | Game engagement |
| `DateIdeaAccepted` | 1.0 | `{ ideaType }` | Date plan accepted |
| `DateIdeaRejected` | 0.0 | `{ ideaType }` | Date plan rejected |
| `ChatNoteLove` | 1.0 | — | Love reaction on AI note |
| `MessageLove` | 1.0 | — | Love reaction on message |
| `ExplicitFeedback` | 0.0–1.0 | `{ rating }` | User-submitted feedback (normalized 1-5 → 0-1) |

#### Game-Derived Signals

| EventType | EventValue | Metadata | Purpose |
|-----------|------------|----------|---------|
| `KnowMeDisclosureDepth` | completion % | `{ rounds }` | Know Me game depth |
| `RedFlagGameDepth` | completion % | `{ rounds }` | Red Flag game depth |
| `FlagAgreementRate` | 0.0–1.0 | — | Red Flag alignment score |

#### Trial End Reasons

When user ends trial via END button, reason is logged:

| EventType | EventValue | Purpose |
|-----------|------------|---------|
| `TrialEndedNoSpark` | 1.0 | Chemistry signal |
| `TrialEndedWrongTiming` | 1.0 | Context signal |
| `TrialEndedNotMyType` | 1.0 | Type mismatch |
| `TrialMessageCount` | count | Engagement during trial |

#### Voice Signals

| EventType | EventValue | Purpose |
|-----------|------------|---------|
| `VoiceNoteListenComplete` | 1.0 | Played voice note to end |
| `MutualVoiceExchange` | 1.0 | Both users sent voice notes |

#### Safety Signal

| EventType | EventValue | Metadata | Purpose |
|-----------|------------|----------|---------|
| `UserFlagged` | 1.0 | `{ reason }` | **Trust score only** — never ECHO compatibility |

**Important:** `UserFlagged` feeds trust scoring but is **excluded** from ECHO preference learning.

---

## Signal Usage in ECHO

See: [`docs/systems/echo/signals.md`](../../systems/echo/signals.md)

### Primary Outcome Proxy

`TimeToFirstMessageMs` — Speed to first message is the strongest compatibility signal. All other signals train toward predicting this.

### Preference Signals

ECHO learns:
- Which **foundational alignments** predict fast engagement
- Which **conversation styles** lead to trials accepted
- Which **game behaviors** correlate with Find Love
- Which **tile affinities** predict mutual voice exchanges

### Signal Weights

Current formula weights (June 4, 2026):

| Signal | Weight | Component |
|--------|--------|-----------|
| TrialAccepted | 0.20 | ConnectionScore |
| TrialContinued | 0.20 | ConnectionScore |
| TimeToFirstMessageMs (fast) | 0.15 | ConnectionScore |
| ChatDepthMessages | 0.10 | ConnectionScore |
| VoiceNoteListenComplete | 0.05 | ConnectionScore |
| MutualVoiceExchange | 0.08 | ConnectionScore |
| LoveReactions | 0.07 | ConnectionScore |

See: [`backend/WovenBackend/Services/Matchmaking/ConnectionScoreBatchWorker.cs`](../../../backend/WovenBackend/Services/Matchmaking/ConnectionScoreBatchWorker.cs)

---

## Event Hygiene Rules

### DO

- ✅ Use constants from `AnalyticsEvents` or `MatchSignalEventTypes`
- ✅ Keep properties <1KB per event
- ✅ Include `matchId` / `candidateId` in properties for joins
- ✅ Log errors but never throw from tracking code
- ✅ Use `await` pattern (fire-and-forget runs in `Task.Run`)

### DON'T

- ❌ Store PII in event properties (no names, emails, addresses)
- ❌ Log raw passwords, tokens, or sensitive credentials
- ❌ Block user actions on analytics failures
- ❌ Create custom event types without adding to constants
- ❌ Track events in tight loops (batch if >100/sec)

---

## Schema Reference

### UserInteractionLog

```sql
CREATE TABLE user_interaction_logs (
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL REFERENCES users(id),
    event_type VARCHAR(50) NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    context JSONB NOT NULL DEFAULT '{}',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_user_interaction_logs_user_occurred 
    ON user_interaction_logs(user_id, occurred_at DESC);
```

### AnalyticsEvent

```sql
CREATE TABLE analytics_events (
    id BIGSERIAL PRIMARY KEY,
    user_id_hash VARCHAR(64),      -- SHA-256 hashed
    session_id VARCHAR(100),
    event_type VARCHAR(100) NOT NULL,
    properties JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_analytics_events_created 
    ON analytics_events(created_at DESC);
CREATE INDEX idx_analytics_events_type_created 
    ON analytics_events(event_type, created_at DESC);
```

### MatchSignalLog

```sql
CREATE TABLE match_signal_logs (
    id BIGSERIAL PRIMARY KEY,
    viewer_id INT NOT NULL,
    candidate_id INT NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    event_value REAL NOT NULL,
    metadata_json TEXT,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_match_signal_logs_viewer_candidate 
    ON match_signal_logs(viewer_id, candidate_id, occurred_at DESC);
CREATE INDEX idx_match_signal_logs_type_occurred 
    ON match_signal_logs(event_type, occurred_at DESC);
```

---

## Related Docs

- [Analytics System Overview](./README.md)
- [Retention Tracking](./retention.md)
- [Workers](./workers.md)
- [Privacy Approach](./privacy.md)
- [Implementation Guide](./implementation.md)
- [ECHO Signals](../../systems/echo/signals.md)
