# Feature Index — QA Quick Reference

**Purpose:** Complete inventory of Woven features with implementation status and acceptance criteria.  
**Source:** Compiled from [docs/INDEX.md](../docs/INDEX.md) and feature documentation  
**Last Updated:** 2026-10-08  
**Status:** Active

---

## 📱 USER-FACING FEATURES

### F-001: Onboarding
**What:** User registration, profile setup, foundational questions  
**Status:** ✅ Implemented  
**Docs:** [features/onboarding/](../docs/features/onboarding/)

**Key Flows:**
- Phone number verification
- Profile creation (name, DOB, gender, photos)
- Foundational questions (5-question flow)
- Vector embedding generation on completion

**Endpoints:**
- `POST /auth/send-otp`
- `POST /auth/verify-otp`
- `POST /onboarding/details`
- `POST /onboarding/foundational`

**Acceptance Criteria:**
- [ ] Phone verification within 60s
- [ ] Profile photo upload (1-6 images)
- [ ] All 5 foundational questions answered
- [ ] User vector generated before Moments access
- [ ] Minimum age 18 enforced

**Test Coverage:** ⏳ Pending

---

### F-002: Moments (Daily Deck)
**What:** Daily curated candidate deck with swipe responses  
**Status:** ✅ Implemented  
**Docs:** [features/moments/](../docs/features/moments/)

**Key Flows:**
- Daily deck generation (03:30 UTC)
- Swipe responses (◈ MAGICAL / ◇ LOGICAL / SKIP)
- Match creation on mutual like
- Balloon state management

**Endpoints:**
- `GET /moments` (Deck + Drawn tabs)
- `POST /moments/respond`
- `GET /moments/stats`

**Acceptance Criteria:**
- [ ] Exactly 5 candidates per deck
- [ ] Deck refreshes daily at 03:30 UTC
- [ ] No duplicate candidates in same deck
- [ ] No self-matches
- [ ] Match created only on compatible responses
- [ ] Balloon expires 36h after match creation

**Test Coverage:** ⏳ Pending  
**Product Rules:** MATCH-001 through MATCH-006, BALLOON-001 through BALLOON-004, DECK-001 through DECK-005

---

### F-003: Sparks (Economy)
**What:** Daily spark earning and spending for Drawn actions  
**Status:** ✅ Implemented (CL-005 fixed critical bug)  
**Docs:** [features/sparks/](../docs/features/sparks/)

**Key Flows:**
- Initial grant (5 sparks on signup)
- Daily earning (lazy, +5 sparks, max 10)
- Spending (1 spark per Liked You action)
- Ghost refund (0.5 sparks on no-message match close)

**Endpoints:**
- `GET /me/sparks` (balance)
- Spending integrated into `POST /moments/respond`

**Acceptance Criteria:**
- [x] New user receives exactly 5.0 sparks (FIXED: CL-005)
- [ ] Daily earn caps at 10.0 sparks
- [ ] Drawn action costs 1.0 spark
- [ ] Ghost refund grants 0.5 sparks
- [ ] Insufficient sparks prevents action (returns error)

**Test Coverage:** Partial (CL-005 fix verified)  
**Product Rules:** SPARK-001 through SPARK-006

---

### F-004: Chats (Messaging + Trial)
**What:** Real-time messaging with trial period and Find Love unlock  
**Status:** ✅ Implemented  
**Docs:** [features/chats/](../docs/chats/)

**Key Flows:**
- Balloon pop → chat unlocked
- Trial starts when both users open chat
- Trial decision (CONTINUE / END / BLOCK)
- Find Love final stage
- Voice messages

**Endpoints:**
- `POST /matches/{id}/pop` (balloon pop)
- `GET /chats/{threadId}` (messages)
- `POST /chats/{threadId}/send`
- `POST /chats/{threadId}/voice-message`
- `POST /chats/{threadId}/trial-decision`
- `POST /chats/{threadId}/messages/{id}/voice-listened`

**Acceptance Criteria:**
- [ ] Trial starts only when BOTH users open chat
- [ ] Trial duration = 3 minutes
- [ ] Trial decision required before Find Love
- [ ] BLOCK creates Block record + closes match
- [ ] Voice messages upload to Azure Blob
- [ ] Listen tracking recorded for ECHO

**Test Coverage:** ⏳ Pending  
**Product Rules:** TRIAL-001 through TRIAL-006

---

### F-005: Games (Interactive Chat)
**What:** AI-powered interactive games during chat (KnowMe, RedGreenFlag)  
**Status:** ✅ Implemented  
**Docs:** [features/games/](../docs/features/games/)

**Key Flows:**
- Game initiation from chat
- Turn-based gameplay
- AI-generated questions/scenarios
- Completion tracking

**Endpoints:**
- `POST /games/knowme/start`
- `POST /games/knowme/answer`
- `POST /games/redgreenflag/start`
- `POST /games/redgreenflag/answer`

**Acceptance Criteria:**
- [ ] Rate limiting per user/match
- [ ] AI calls tracked for cost monitoring
- [ ] Game state persisted
- [ ] Completion triggers ECHO signal

**Test Coverage:** ⏳ Pending

---

### F-006: Matches (Profiles + Explanations)
**What:** Match profile viewing and AI-generated explanations  
**Status:** ✅ Implemented  
**Docs:** [features/matches/](../docs/features/matches/)

**Key Flows:**
- Match explanation generation
- Profile viewing
- Match type display (PURE vs EDGE)
- Bridge question suggestions

**Endpoints:**
- `GET /matches/{id}` (match details)
- `GET /matches/{id}/profile` (partner profile)
- `GET /matches/{id}/explanation`

**Acceptance Criteria:**
- [ ] Explanation shows "why matched" narrative
- [ ] PURE/EDGE badge displayed correctly
- [ ] Bridge question suggested (3-pillar comparison)
- [ ] NO raw compatibility scores shown
- [ ] Profile hides sensitive PII

**Test Coverage:** ⏳ Pending  
**Product Rules:** PRIVACY-002, PRIVACY-003

---

### F-007: Commons (Content Feed)
**What:** User-generated content tiles with Orbit reactions  
**Status:** ✅ Implemented  
**Docs:** [features/commons/](../docs/features/commons/)

**Key Flows:**
- Tile creation (text, image, voice)
- Feed browsing
- Orbit reactions (platform signal, not shown to users)
- Tile deletion

**Endpoints:**
- `GET /commons` (feed)
- `POST /commons/tiles`
- `DELETE /commons/tiles/{id}`
- `POST /commons/tiles/{id}/orbit`

**Acceptance Criteria:**
- [ ] Moderation on tile creation
- [ ] Media upload to Azure Blob
- [ ] Orbit count NOT shown to tile author
- [ ] CLIP embeddings generated for visual tiles
- [ ] Feed ranking by recency + engagement

**Test Coverage:** ⏳ Pending

---

### F-008: Profile (User Settings)
**What:** View/edit profile, settings, my tiles  
**Status:** ✅ Implemented  
**Docs:** [features/profile/](../docs/features/profile/)

**Key Flows:**
- Profile viewing
- Photo management
- Settings (notifications, privacy)
- My tiles view

**Endpoints:**
- `GET /me` (own profile)
- `PATCH /me` (update profile)
- `POST /me/photos`
- `DELETE /me/photos/{id}`
- `GET /me/settings`
- `PATCH /me/settings`

**Acceptance Criteria:**
- [ ] Photo limit enforced (1-6 images)
- [ ] Settings persist correctly
- [ ] Profile changes trigger re-embedding
- [ ] Notification preferences respected

**Test Coverage:** ⏳ Pending

---

### F-009: Assistant (Woven AI)
**What:** Unified AI assistant for app guidance  
**Status:** ⚠️ Partially Implemented  
**Docs:** [features/assistant/](../docs/features/assistant/)

**Key Flows:**
- Ask questions about matches
- Get dating advice
- Understand app features

**Endpoints:**
- `POST /assistant/ask`

**Acceptance Criteria:**
- [ ] Context-aware responses
- [ ] No PII leakage in responses
- [ ] Rate limiting enforced

**Test Coverage:** ⏳ Pending  
**Note:** Endpoint exists but feature incomplete

---

### F-010: Notifications
**What:** Web push notifications for matches, messages, events  
**Status:** ✅ Implemented (completed 2026-06-04)  
**Docs:** [features/notifications/](../docs/features/notifications/)

**Key Flows:**
- VAPID key generation
- Subscription management
- Push delivery (matches, messages, Send Spark reminders)
- Service worker handling

**Endpoints:**
- `GET /push-notifications/vapid-public-key`
- `POST /push-notifications/subscribe`
- `POST /push-notifications/unsubscribe`

**Acceptance Criteria:**
- [x] Web Push standard compliance
- [x] Service worker registration
- [x] Notification click → app navigation
- [ ] Delivery tracking
- [ ] Unsubscribe on logout

**Test Coverage:** ⏳ Pending

---

## ⚙️ BACKEND SYSTEMS

### S-001: ECHO (Matchmaking AI)
**What:** ML pipeline for candidate ranking and weight learning  
**Status:** ✅ Implemented  
**Docs:** [systems/echo/](../docs/systems/echo/)

**Components:**
- Candidate pool filtering
- 14-component scoring formula
- ConnectionScore batch worker (nightly)
- WeightLearning batch worker (weekly)
- Match explanations

**Acceptance Criteria:**
- [ ] All 14 components calculated correctly
- [ ] Batch workers run on schedule
- [ ] Trust threshold enforced (≥0.25)
- [ ] Gender reciprocity respected
- [ ] Blocked users excluded

**Test Coverage:** ⏳ Pending  
**Product Rules:** TRUST-001, TRUST-002

---

### S-002: Authentication
**What:** JWT-based auth with HttpOnly cookies  
**Status:** ✅ Implemented  
**Docs:** [systems/auth/](../docs/systems/auth/)

**Components:**
- OTP generation/verification
- JWT token creation
- Cookie-based sessions (production)
- DevAuth endpoints (development only)

**Acceptance Criteria:**
- [ ] JWT expires after 60 minutes (configurable)
- [ ] HttpOnly cookies in production
- [ ] DevAuth unavailable in production
- [ ] Token refresh not yet implemented

**Test Coverage:** ✅ 2/2 JWT tests pass  
**Product Rules:** AUTH-001, AUTH-002, AUTH-003

**Security Note:** DevAuth endpoints protected by IsDevelopment() check (runtime, not compile-time)

---

### S-003: Embeddings
**What:** Multi-modal embedding generation (text, image, voice)  
**Status:** ✅ Implemented  
**Docs:** [systems/embeddings/](../docs/systems/embeddings/)

**Components:**
- text-embedding-3-small (OpenAI)
- CLIP embeddings (visual content)
- Voice note embeddings
- Preference embedding extraction (planned)

**Acceptance Criteria:**
- [ ] User vector generated on onboarding completion
- [ ] Tile embeddings generated on creation
- [ ] Embedding dimensions consistent (512D)

**Test Coverage:** ⏳ Pending

---

### S-004: Media Storage
**What:** Azure Blob Storage integration for photos/voice/video  
**Status:** ✅ Implemented  
**Docs:** [systems/media/](../docs/systems/media/)

**Components:**
- SAS token generation
- Upload confirmation
- Content delivery
- Media moderation

**Acceptance Criteria:**
- [ ] SAS tokens expire after 15 minutes
- [ ] Upload confirmed before DB record created
- [ ] Media URLs use CDN
- [ ] Moderation scans all uploads

**Test Coverage:** ⏳ Pending

---

### S-005: Trust & Verification
**What:** Trust scoring and user verification  
**Status:** ✅ Implemented  
**Docs:** [systems/trust/](../docs/systems/trust/)

**Components:**
- Trust score calculation (0.0-1.0)
- Verification badges
- Report handling
- Block management

**Acceptance Criteria:**
- [ ] Trust score ≥0.25 for matching
- [ ] Blocked users never appear in candidates
- [ ] Reports create moderation tickets

**Test Coverage:** ⏳ Pending  
**Product Rules:** TRUST-001 through TRUST-004

---

### S-006: Queue (Background Workers)
**What:** Scheduled and on-demand background jobs  
**Status:** ✅ Implemented  
**Docs:** [systems/queue/](../docs/systems/queue/)

**Workers:**
- DailyDeckOrchestrator (03:30 UTC)
- ConnectionScoreBatchWorker (03:50 UTC)
- WeightLearningBatchWorker (Sun 04:00)
- BalloonExpiryWorker (every 60s)
- CoachingSummaryWorker (every 6h)

**Acceptance Criteria:**
- [ ] Workers run on schedule (verified by logs)
- [ ] Graceful shutdown on cancellation
- [ ] Alerting on worker failures
- [ ] Redis distributed locks prevent overlap

**Test Coverage:** ⏳ Pending

**Known Issues:**
- CfScoreBatchWorker duplicate schedule (05:00 = 03:00)
- Workers ignore CancellationToken
- No alerts for 2x expected runtime

---

### S-007: Analytics
**What:** Behavior tracking with PII sanitization  
**Status:** ✅ Implemented  
**Docs:** [systems/analytics/](../docs/systems/analytics/)

**Components:**
- MatchSignalLogs (90-day retention)
- Event tracking
- PII hashing (SHA-256)
- Retention policies

**Acceptance Criteria:**
- [ ] PII hashed before analytics storage
- [ ] 90-day retention enforced
- [ ] No raw user data in analytics DB

**Test Coverage:** ⏳ Pending  
**Product Rules:** RETENTION-001, RETENTION-003

---

### S-008: Coaching
**What:** Weekly AI-generated coaching summaries  
**Status:** ✅ Implemented  
**Docs:** [systems/coaching/](../docs/systems/coaching/)

**Components:**
- Weekly summary generation
- Delivery tracking
- Opt-out support

**Acceptance Criteria:**
- [ ] Summaries generated weekly
- [ ] Delivery tracked (DeliveredAt timestamp)
- [ ] Opt-out respected

**Test Coverage:** ⏳ Pending

---

### S-009: Moderation
**What:** AI-powered content moderation  
**Status:** ✅ Implemented  
**Docs:** [systems/moderation/](../docs/systems/moderation/)

**Components:**
- OpenAI Moderation API
- Manual review queue
- Flagging system

**Acceptance Criteria:**
- [ ] All user content moderated before publish
- [ ] Flagged content reviewed by moderation queue
- [ ] Repeat offenders auto-suspended

**Test Coverage:** ⏳ Pending

---

### S-010: Caching
**What:** Redis caching layer  
**Status:** ✅ Implemented  
**Docs:** [systems/caching/](../docs/systems/caching/)

**Use Cases:**
- SignalR backplane
- Rate limiting
- Distributed locks
- Session storage

**Acceptance Criteria:**
- [ ] Cache hit rate monitored
- [ ] TTL policies enforced
- [ ] Fallback on cache miss

**Test Coverage:** ⏳ Pending

---

## 🚧 INCOMPLETE / PLANNED FEATURES

### F-011: Seasons (Planned)
**What:** Seasonal feature themes  
**Status:** ❌ Not Implemented  
**Docs:** [systems/seasons/](../docs/systems/seasons/)

### F-012: Venues (Planned)
**What:** Date venue recommendations  
**Status:** ❌ Not Implemented  
**Docs:** [systems/venues/](../docs/systems/venues/)

### F-013: Feedback (Partial)
**What:** Post-date feedback collection  
**Status:** ⚠️ Partially Implemented  
**Docs:** [systems/feedback/](../docs/systems/feedback/)  
**Note:** Database entities exist, UI incomplete

---

## FEATURE DEPENDENCY MAP

```
Onboarding (F-001)
  └─> Embeddings (S-003)
       └─> ECHO (S-001)
            └─> Moments (F-002)
                 ├─> Matches (F-006)
                 │    └─> Chats (F-004)
                 │         ├─> Games (F-005)
                 │         └─> Trial → Find Love
                 └─> Sparks (F-003)
                      └─> Drawn actions

Commons (F-007)
  ├─> Media (S-004)
  ├─> Moderation (S-009)
  └─> Embeddings (S-003)

Profile (F-008)
  └─> Media (S-004)

All Features
  ├─> Authentication (S-002)
  ├─> Caching (S-010)
  ├─> Trust (S-005)
  └─> Queue (S-006)
```

---

## CHANGE LOG

| Date | Feature | Change | Impact | Approval |
|------|---------|--------|--------|----------|
| 2026-10-08 | F-003 (Sparks) | Fixed double-grant bug (CL-005) | Economy integrity restored | Founder ✓ |
| 2026-10-07 | F-002 (Moments) | Corrected deck size (60→5) | Acceptance criteria updated | Founder ✓ |
| 2026-10-07 | F-004 (Chats) | Corrected balloon lifetime (72h→36h) | Acceptance criteria updated | Founder ✓ |
| 2026-06-04 | F-010 (Notifications) | Web Push implemented | Feature complete | Founder ✓ |

---

## USAGE INSTRUCTIONS

**For Test Planning:**
1. Use feature IDs (F-001, S-001) in test case names
2. Acceptance criteria = test scenarios
3. "⏳ Pending" = no automated coverage yet

**For Regression Testing:**
1. Check dependency map before testing changes
2. Upstream changes affect downstream features
3. Re-test dependent features after fixes

**When Features Change:**
1. Update acceptance criteria in this index
2. Add Change Log entry with founder approval
3. Update affected test cases
4. Mark "Test Coverage" as "🔄 Re-test Required"

**Cross-Reference:**
- Full feature docs: [docs/features/](../docs/features/)
- Full system docs: [docs/systems/](../docs/systems/)
- Product rules: [PRODUCT_RULES_INDEX.md](./PRODUCT_RULES_INDEX.md)
