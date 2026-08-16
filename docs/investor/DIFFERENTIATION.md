# Technical Differentiation

## Overview

Woven's technical differentiation is not a single feature — it is a system of interconnected components that would require significant engineering effort and behavioral data to replicate. Each differentiator below is built and deployed (with noted exceptions where gaps exist). Where a component depends on another, the dependency is described.

---

## 1. Per-User Adaptive ML Weights

Most matching systems apply a single weight vector across all users. ECHO applies a per-user weight vector, initialized to base weights and then adapted via weekly logistic regression.

**Implementation:**
- 16 scoring components with base weights (e.g., pillar=0.19, visual=0.10, voice=0.08, cf=0.03)
- `WeightLearningBatchWorker` runs every Sunday 04:00 UTC
- Algorithm: mini-batch logistic regression, learning rate=0.01, 100 iterations, L2 regularization
- Training data: ConnectionScore labels accumulated per (viewer, candidate) pair over time
- Output: per-user weight adjustments stored and applied to subsequent ECHO scoring runs

**Why it matters:** A user whose high-connection outcomes consistently correlate with voice resonance will, over time, have a higher voice component weight than a user whose outcomes correlate with lifestyle embedding similarity. The system learns what actually predicts connection for each person, not for the average user.

**Dependency:** Per-user weight adaptation becomes meaningful as ConnectionScore labels accumulate. Early users see base weights; adaptation improves with engagement history.

---

## 2. 16-Component Behavioral Scoring

ECHO scores each (viewer, candidate) pair across 16 distinct components. This is not a composite of a single embedding similarity — it is a multi-dimensional evaluation that captures different facets of predicted compatibility.

**Components and base weights:**

| Component | Base Weight | Signal Source |
|---|---|---|
| pillar | 0.19 | AI-generated pillar embeddings (1536-dim) |
| intent | 0.12 | IntentEmbedding (1536-dim) |
| expression | 0.09 | ExpressionEmbedding (1536-dim) |
| style | 0.09 | StyleEmbedding (128-dim) |
| visual | 0.10 | VisualPreferenceService (image embeddings) |
| voice | 0.08 | VoiceEmbedding via SpeechBrain ECAPA-TDNN (192-dim) |
| humor | 0.07 | HumorEmbedding (64-dim) |
| lifestyle | 0.08 | LifestyleEmbedding (128-dim) |
| behavioral_lifestyle | 0.05 | Behavioral signals over time |
| emotional_rhythm | 0.04 | EmotionalRhythmEmbedding (48-dim) |
| attachment | 0.04 | AttachmentProxyEmbedding (4-dim) |
| orbit_gravity | 0.08 | Tile interaction signals (explicit Orbit actions) |
| pulse | 0.06 | Recency-weighted engagement signals |
| cf | 0.03 | Collaborative filtering score |
| shared_tile_affinity | 0.05 | Shared content interaction patterns |
| preference_affinity | 0.04 | Preference signal alignment |

**Why it matters:** A single embedding similarity score cannot distinguish between two users who are intellectually compatible but emotionally mismatched, or compatible in lifestyle but not in humor. Multi-component scoring enables nuanced, multi-dimensional candidate evaluation.

---

## 3. Nine Embedding Modalities

Woven generates 9 types of embeddings per user, each capturing a distinct dimension of identity and compatibility. All are stored in PostgreSQL with pgvector HNSW indexes for fast cosine similarity search.

| Embedding | Dimensions | Generation method |
|---|---|---|
| PillarEmbedding | 1536 | OpenAI text-embedding model on AI-generated pillar profiles |
| ExpressionEmbedding | 1536 | OpenAI text-embedding on expression-style content |
| IntentEmbedding | 1536 | OpenAI text-embedding on intent-related signals |
| StyleEmbedding | 128 | Derived from communication style signals |
| HumorEmbedding | 64 | Derived from humor-tagged content and interactions |
| LifestyleEmbedding | 128 | Derived from lifestyle-related profile and behavioral data |
| EmotionalRhythmEmbedding | 48 | Derived from emotional cadence signals |
| AttachmentProxyEmbedding | 4 | Compact attachment style proxy |
| VoiceEmbedding | 192 | SpeechBrain ECAPA-TDNN on voice note audio |

**Why it matters:** Most apps embed text profiles into a single vector. Woven separates the signal into 9 purpose-specific embeddings, each stored and queried independently. This allows the weight learning system to discover which embedding modality is most predictive for each individual user.

---

## 4. Behavioral Fingerprint (16-dim, 180-day window)

`BehavioralFingerprintService` generates a 16-dimensional vector representation of each user's behavioral history over a rolling 180-day window.

**Key design decisions:**
- Missing signals default to 0.5 (neutral), not 0.0 (absence)
- This prevents the system from penalizing new users or users who haven't yet generated specific signal types
- The 180-day window ensures recent behavior dominates while preserving enough history for pattern recognition
- The fingerprint is used as input to per-user weight adaptation and to the LinUCB contextual bandit

**Why it matters:** A fingerprint based on revealed behavioral patterns captures the "how someone engages" dimension that no questionnaire can measure — response latency patterns, game depth, voice note frequency, orbital engagement with content tiles.

---

## 5. SpeechBrain Voice Embeddings

Voice resonance is a compatibility dimension that no competing platform currently treats as a matching signal. Woven generates 192-dimensional voice embeddings from user voice notes using SpeechBrain's ECAPA-TDNN model.

**Implementation:**
- SpeechBrain runs in a dedicated Azure Container Apps pod (min=max=1, 2 uvicorn workers)
- ECAPA-TDNN model is pre-downloaded at build time (not fetched at runtime)
- Voice note flow: MediaRecorder → SAS token → Azure Blob PUT → confirm → voice embedding generation queued via Azure Service Bus
- The 192-dim VoiceEmbedding is stored in pgvector alongside other modality embeddings
- `MutualVoiceExchange` signal is recorded when both users in a match send voice notes — feeds into ConnectionScore

**Why it matters:** The physical resonance of a voice is a documented factor in interpersonal attraction. It is also impossible to game: users cannot optimize their voice to score better on a metric they cannot see. This makes it a high-quality, low-manipulation signal.

---

## 6. Trial Period Mechanic

When a match opens the chat thread for the first time, a structured commitment window begins:
- Trial starts when the second user opens the thread (not at balloon pop)
- `TrialEndsAt` = now + 3 minutes once both `TrialUserAOpenedAt` and `TrialUserBOpenedAt` are set
- Both users can signal: CONTINUE, END (with reason: no_spark / wrong_timing / not_my_type), or BLOCK
- BLOCK immediately closes the match and creates a Block record
- Trial decisions feed ECHO: TrialRequested (0.10) and TrialAccepted (0.25) are ConnectionScore components

**Why it matters:** The trial period is simultaneously a UX decision (addressing permanent ghosting culture) and a training data decision (generating high-signal commitment labels). A system that can distinguish "matched and ghosted immediately" from "matched, entered trial, and continued" has substantially more nuanced outcome labels than one that treats all matches equivalently.

---

## 7. Find Love + Date Idea Generation

Five minutes after both users first message (not at match formation), Find Love unlocks. The `MatchExplanationService` generates 3 personalized date ideas using OpenAI gpt-4.1-mini.

**Personalization inputs:**
- Past accepted date ideas (from `DateIdeaAccepted` signal log)
- The tone (playful / calm / serious) that preceded the viewer's fastest first messages
- Match context (pillar alignment, location signals)

**Why it matters:** Most apps end at match formation. Woven continues the product experience through the point where users need help moving from app conversation to real-world meeting. The date ideas are not generic suggestions — they are personalized based on revealed behavioral history. This positions Woven as a full-journey product, not just a matching tool.

---

## 8. ChatNotes as Training Signal

When users make a choice on a Moments card, they are required to write a note (20-150 characters). These notes are:
- Never shown to other users
- Never surfaced as feedback
- Stored as background behavioral signal
- Planned input to future embedding generation (PreferenceEmbedding from ChatNotes — worker stub exists, full wiring in progress)

**Why it matters:** The notes capture the reasoning behind choices in natural language, at the moment of decision. This is qualitatively different from a post-hoc survey: it is in-context, time-stamped, and associated with a specific candidate profile. When the PreferenceEmbedding worker is fully wired, these notes will inform a preference signal that updates with each choice a user makes.

---

## 9. LinUCB Contextual Bandit for Deck Ordering

ECHO scores candidates using the 16-component system, but the daily deck order is determined by a LinUCB contextual bandit. LinUCB is an upper confidence bound algorithm that balances:
- **Exploitation:** showing candidates that the model predicts will connect well based on current weight estimates
- **Exploration:** showing candidates outside the model's high-confidence zone, to gather signal and avoid filter bubbles

**Why it matters:** Pure exploitation produces filter bubbles: the model shows more of what it already knows the user responds to, at the cost of never discovering new compatibility dimensions. Pure exploration wastes user attention. LinUCB finds a principled balance, and the exploration behavior generates the diverse signal needed for per-user weight learning to remain effective over time.

---

## 10. Per-Viewer Tone Personalization in Explanations

`MatchExplanationService` reads the viewer's historical `TimeToFirstMessageMs` signal to determine explanation tone:
- Low median first-message latency (responds quickly) → playful, lighter tone
- High median first-message latency (deliberate, slower to respond) → calm or serious tone

The explanation for why ECHO recommended a match is then generated in the appropriate tone. Users never see this selection happening — they experience the explanation as naturally fitting their communication style.

**Why it matters:** Explanation tone is a trust signal. A playful explanation delivered to a user who responds to matches in a deliberate, considered manner creates a tone mismatch that erodes trust in the recommendation. Adaptive tone is invisible UX that makes ECHO's recommendations feel more credible without any visible mechanism.

---

## Differentiator Summary

| Differentiator | Built | Dependent on |
|---|---|---|
| Per-user adaptive ML weights | Yes | ConnectionScore label accumulation |
| 16-component behavioral scoring | Yes | Embedding generation pipeline |
| 9 embedding modalities | Yes | Ongoing embedding batch workers |
| Behavioral fingerprint (16-dim, 180-day) | Yes | Signal accumulation over time |
| SpeechBrain voice embeddings | Yes | User voice note engagement |
| Trial period mechanic | Yes | — |
| Find Love + date idea generation | Yes | — |
| ChatNotes as training signal | Partial | PreferenceEmbedding worker (in progress) |
| LinUCB contextual bandit | Yes | Behavioral fingerprint data |
| Per-viewer tone personalization | Yes | TimeToFirstMessageMs signal history |
