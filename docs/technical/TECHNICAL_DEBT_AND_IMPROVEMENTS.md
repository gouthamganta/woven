# Woven — Technical Debt & Known Improvements

This document catalogs known gaps, deferred work, and intentional tradeoffs in the current Woven codebase. Items are organized by category. Each entry includes what is missing, why it matters, and (where known) what is needed to close the gap.

---

## AI / ML Gaps

### CfScore Batch Job Not Running

- **What exists**: `CollaborativeFilteringService` (computes collaborative filtering scores, `CfScore`) and `CfBatchWorker` (intended batch runner) both exist.
- **What's missing**: `CfBatchWorker` is not registered in the scheduler in `Program.cs`. CfScores are never computed in the current build.
- **Impact**: The `SharedTileAffinity` matchmaking scoring component depends on CfScore data. Because CfScores are not computed, this component cannot contribute to match scoring — it either returns zero or is skipped entirely.
- **To close**: Register `CfBatchWorker` in `Program.cs` with an appropriate schedule (e.g., nightly). Verify `SharedTileAffinity` reads the populated scores correctly.

### SharedTileAffinity Not Computing

- **Root cause**: Downstream of the CfBatchWorker gap above. No separate fix is possible until CfScores are populated.
- **Impact**: One of the 16 match scoring components is effectively inactive.

### PreferenceEmbedding from ChatNotes Not Wired

- **What exists**: A worker stub for generating preference embeddings from `ChatNote` records.
- **What's missing**: The stub is not connected to the embedding pipeline — it is never triggered. `ChatNote` data is recorded but not converted into a preference embedding vector.
- **Impact**: An entire signal dimension (revealed preferences inferred from in-chat note patterns) is missing from the embedding layer.
- **To close**: Wire the stub worker to the Service Bus queue or a scheduled trigger. Verify the output vector is stored and consumed by `UserVectorBuilder`.

### Ambition Pillar Not Covered by Any Foundational Question

- **What exists**: `AiProfileService` scores 8 pillars. `FoundationalQuestionBank` provides the questions used to generate pillar signals.
- **What's missing**: No foundational question currently covers the Ambition pillar. The pillar exists in the scoring model but has no foundational question input.
- **Impact**: Ambition pillar scores are generated from limited signal. Matches where ambition alignment is a key factor may be scored inaccurately.
- **To close**: Add one or more foundational questions targeting the Ambition pillar and register them in `FoundationalQuestionBank`.

---

## UX / Product Gaps

### "Your Turn" Chat List Indicator

- **Status**: Designed. Not built.
- **Description**: A visual indicator in the chat list showing which threads are waiting for the logged-in user to reply.
- **To close**: Add a computed property (or backend field) tracking whether the most recent message in a thread was sent by the other user. Surface it as a badge or highlight in the chat list component.

### Active / Online Indicator

- **Status**: Designed. Not built.
- **Description**: A presence indicator showing whether a match is currently active.
- **To close**: Requires a presence tracking mechanism (e.g., periodic heartbeat endpoint, WebSocket presence, or Redis TTL-based last-seen). Not yet architected.

### Horoscope Onboarding Field

- **Status**: Designed. Not built.
- **Description**: A horoscope / birth chart field in the onboarding flow.
- **To close**: Add the field to the onboarding form, persist it to `user_optional_fields` (or a dedicated column), and register it as a signal if horoscope compatibility is intended to contribute to scoring.

---

## Security Gaps

### JWT in localStorage

- **What it is**: Woven JWTs are currently stored in `localStorage` on the frontend.
- **Why it's a gap**: `localStorage` is accessible to JavaScript. If the app has an XSS vulnerability, a JWT in `localStorage` can be exfiltrated. An httpOnly cookie cannot be read by JavaScript.
- **Documented as**: dev convenience.
- **To close**: Move JWT storage to an httpOnly, SameSite=Strict (or Lax) cookie. Update CORS and cookie settings on the backend. This is a pre-production requirement.

### chat_messages.body Not Encrypted

- **What it is**: The `chat_messages.body` column stores message text in plaintext.
- **Why it's not encrypted**: A `CHECK` constraint enforces 1–1000 character length. AES-256-GCM ciphertext for any non-trivial message exceeds 1000 characters, so applying encryption would violate the constraint.
- **Documented in**: `WovenDbContext` comments.
- **To close**: Remove or relax the `CHECK` constraint (or change it to check the decrypted length at the application layer), then apply `EncryptionService` to `body` at write/read time. Requires a migration and a one-time re-encryption of existing rows.

### Push Notification Service Worker Not Deployed

- **What exists**: VAPID public/private keys configured in `appsettings.json`. `NotificationService` with dispatch logic. `FeedbackTriggerWorker`, `NudgeService`, and `WeeklyDigestWorker` all call `INotificationService`.
- **What's missing**: The frontend service worker (`sw.js` or equivalent) is not deployed. Without it, the browser cannot receive Web Push messages — push notifications are entirely non-functional.
- **To close**: Write and register a service worker in the Angular frontend. Wire the Web Push subscription endpoint on the backend (`NotificationService` already exists). Test with VAPID keys in a staging environment.

---

## Infrastructure Gaps

### Replicate API Integration Not Confirmed

- **What exists**: `replicate_api_token` is defined as a Terraform variable and provisioned as a Container App secret.
- **What's missing**: No service file using the Replicate API was identified in reviewed source code. The purpose and calling service are unknown.
- **Risk**: An API key is being provisioned and billed (potentially) for an integration whose usage is unverified.
- **To close**: Audit the codebase for Replicate API calls. Either document the usage explicitly or remove the Terraform variable and secret if the integration was abandoned.

### SpeechBrain Pod Fixed at 1 Replica

- **What it is**: The SpeechBrain ECAPA-TDNN sidecar Container App is configured with a fixed replica count of 1. No autoscaling rules are defined.
- **Impact**: High voice note volume could saturate the single pod, causing embedding queue depth to grow and voice embedding latency to spike.
- **To close**: Add a KEDA or Azure Container Apps autoscaling rule tied to the Service Bus `tile-embedding` queue depth, or to CPU utilization of the sidecar pod.

---

## Intentional Tradeoffs

These are decisions made deliberately. They are documented here for transparency, not because they need to be changed.

### WovenDbContext Registers All Entities in One File

- All `DbSet<T>` properties and `modelBuilder` configurations live in a single `WovenDbContext.cs`.
- **Tradeoff**: the file is large, but it is cohesive — one file to open to understand the full schema.
- **Alternative considered**: splitting into partial classes per domain. Deferred in favor of simplicity.

### Batch Workers Share a Single Workers Pod

- All background workers (`WeightLearningBatchWorker`, `ConnectionScoreBatchWorker`, `EmbeddingBatchWorker`, etc.) run in the same Azure Container App.
- **Tradeoff**: simple deployment, no distributed coordination overhead. If one worker saturates CPU, it affects others.
- **Alternative considered**: separate Container App per worker. Not pursued — current worker load does not justify the operational overhead.

### Content Moderation Disabled in Dev

- `IsModerationEnabled = false` in the development environment.
- **Tradeoff**: developers can create test profiles and content without triggering moderation blocks. Saves OpenAI moderation API calls in dev.
- **Requirement**: this flag must be set to `true` before production deployment.

---

## Improvement Opportunities

These are not bugs or gaps — they are areas where the current implementation is functional but could be improved.

### ConnectionScore Weights Are Not A/B Tested

- The 7 signal weights in the `ConnectionScore` composite label formula are fixed constants.
- Weight values could be validated or tuned via A/B experiments if cohort-level outcome data becomes available.

### LinUCB Alpha Parameter Is Not Environment-Tunable

- `LinUCB` uses an exploration parameter `alpha` that controls the explore/exploit balance.
- `alpha` is currently a hardcoded constant. There is no environment variable or appsettings key to tune it without a code change.
- Exposing `alpha` as a configuration value would allow tuning without deployment.

### /health Endpoint Is Liveness Only

- The backend `/health` endpoint returns 200 if the process is running. It does not check PostgreSQL, Redis, or Service Bus connectivity.
- A deep health check that verifies database connectivity and cache availability would give more meaningful signal during deployment smoke checks.
