# Financial and Growth Thinking Framework

This document does not contain projected revenue, user counts, or churn rates. Those require beta data that does not yet exist. What it contains: the framework for understanding Woven's unit economics, cost structure, and leading indicators — grounded in what is actually built.

Fill in the `[To be measured in beta]` placeholders as real data accumulates.

---

## Unit Economics Framework

### AI Cost: OpenAI

Woven uses `gpt-4.1-mini` (as configured in `appsettings.json`) for four surfaces:

| Surface | Service | Trigger |
|---|---|---|
| Pillar scoring | `AiProfileService` | Profile update or periodic refresh |
| Match explanations + date ideas | `MatchExplanationService` | Find Love unlock per match pair |
| Games | `KnowMeAgent`, `RedGreenFlagAgent` | Each game round initiated in chat |
| Dynamic intake rewrite | `OpenAiDynamicIntakeRewriteService` | Onboarding intake cycle |

A `CircuitBreakerService` enforces a `DailyBudgetUsd=$50` hard cap. When the daily budget is exhausted, AI-dependent features degrade gracefully — the system does not go down. This cap is the single most important number in Woven's current cost model.

At scale, OpenAI cost scales roughly linearly with: (active users) × (average matches receiving explanation per day) × (average game rounds per active user per day). The exact per-call cost depends on prompt length and completion length — measure these in beta and project from actuals.

### AI Cost: SpeechBrain (Voice Embeddings)

SpeechBrain ECAPA-TDNN runs as a self-hosted sidecar on Azure Container Apps. Voice embedding generation has no per-call API cost beyond the Container App compute. This is a fixed infrastructure cost, not a variable per-user cost. As voice note volume grows, the Container App can scale horizontally — cost grows as a step function tied to replica count, not as a per-call charge.

This is a deliberate architectural choice: voice is a core differentiator (192-dimensional vocal-rhythm embeddings), and self-hosting removes the per-user variable cost that would otherwise make voice a per-match expense.

### Spark Economy as a Natural Gate

The spark economy (5 sparks/day, wallet max 10, 1 spark per Drawn action, 0.5 ghost refund on unmatched connections with zero messages) acts as a soft rate limiter on the highest-cost user action: extending a balloon and triggering the match pipeline. This limits the number of AI calls per user per day without a hard paywall.

Current implementation: no hard paywalls. The spark economy is the only soft gate.

---

## Key Leading Indicators

These are the metrics that predict long-term retention and connection quality, derived from the signal architecture in `ConnectionScoreBatchWorker` and `WeightLearningBatchWorker`.

### Tier 1: Connection quality signals (highest predictive value)

**Trial continuation rate**
Formula: (TrialAccepted events) / (TrialRequested events)
Why it matters: this is the highest-weighted signal in ConnectionScore (0.25). A user who chooses CONTINUE after a 3-minute real conversation is a qualitatively different engagement event than a swipe. Low trial continuation rate → match quality problem → deck quality problem → ECHO needs more data or the embedding pipeline has drift.

**Find Love conversion rate**
Formula: (matches reaching BothMessagedAt) / (matches where BalloonPopped)
Why it matters: measures how many matches progress past initial connection into sustained conversation. This is the metric most correlated with the product working.

**Date idea acceptance rate**
Formula: (DateIdeaAccepted events) / (Find Love unlocked events × 3 ideas presented)
Why it matters: measures whether `MatchExplanationService`'s tone adaptation and context reasoning are producing useful output. A low acceptance rate suggests the AI is generating generic ideas, not personalized ones.

### Tier 2: Engagement depth signals

**TimeToFirstMessageMs distribution**
Not just the mean — look at the distribution. A bimodal distribution (some users message immediately, others never message) is worse than a tight normal distribution centered on a reasonable latency. ECHO's primary outcome proxy is TimeToFirstMessageMs.

**ConversationDepth distribution**
`ChatDepthMessages` signal. How many messages are exchanged per match? A healthy distribution skews right (some very deep conversations). Median matters more than mean here (a few very active users can inflate the mean).

**ConnectionScore accumulation rate**
How fast is the average active user generating ConnectionScore labels? A user accumulating labels at a healthy rate is actively using the product. Below 10 labels, ECHO runs on default weights — this user is not yet in the flywheel.

### Tier 3: ECHO health signals

**WeightLearningBatchWorker eligibility rate**
Formula: (users with ≥10 qualifying ConnectionScores where MinConnectionScore=0.08) / (active users in the past 30 days)
Why it matters: this is the "flywheel engaged" metric. Until a user crosses this threshold, they are receiving generic ECHO matching. When the eligibility rate grows, the product's core differentiation is actually working.

**OpenAI DailyBudgetUsd utilization**
How much of the $50/day budget is being consumed? Near-zero means the AI surfaces are not being triggered (user activity too low or sessions too short). Near-$50 means the circuit breaker is approaching — either reduce AI call frequency or raise the budget.

All values: [To be measured in beta]

---

## The Flywheel Threshold

This is the most important structural fact about ECHO's economics:

`WeightLearningBatchWorker` requires:
- `MinSamples = 10` qualifying ConnectionScores per user
- `MinConnectionScore = 0.08` (a ConnectionScore below this floor is excluded as noise)

Below this threshold, every user's deck is generated using the system-default weights in `MatchScoringService`. The 16 scoring components run, but without per-user personalization.

Above this threshold, Sunday's batch job fits a logistic regression model on that user's ConnectionScore history and writes personalized weights back to the database. From this point, ECHO is genuinely personalized.

Implication for early retention strategy: the flywheel "kicks in" after a user accumulates 10 meaningful match interactions. This gives a concrete early retention milestone: get users to 10 ConnectionScore-qualifying matches. Below that threshold, the product must compete on product mechanics alone (Trial, Find Love, Games, voice). Above it, the deck itself becomes a retention driver.

---

## Infrastructure Cost Scaling

### Linear scaling (cost grows proportionally with users/activity)

- **OpenAI API calls**: scales with (users × sessions × AI-triggered actions). Cap is the DailyBudgetUsd circuit breaker.
- **Azure Blob Storage**: scales with (voice notes stored × photo/media volume). Blob Storage pricing is linear on GB-months stored plus per-10K transactions.
- **Azure Service Bus**: scales with message volume. Used for async signal recording and batch job triggering.
- **Outbound bandwidth**: scales with API response volume, voice note serving, photo serving.

### Step-function scaling (cost jumps at capacity thresholds)

- **Redis Standard C1**: single fixed tier currently. Upgrade to C2 or C3 when cache hit rate degrades under load. Each tier jump is a step-function cost increase.
- **PostgreSQL Flexible Server tier**: currently sized for beta load. Moving from Burstable to General Purpose is a step-function cost jump driven by concurrent connection count and query complexity.
- **Container App replicas**: each service (frontend, backend, workers, speechbrain) autoscales horizontally. Each additional replica is a fixed compute cost — CPU and memory allocation per replica. The speechbrain sidecar is the most compute-intensive.
- **pgvector index size**: as embedding volume grows, the HNSW/IVFFlat index on vector columns consumes memory and affects query latency. Index rebuild or parameter retuning may be needed at scale.

### Fixed costs (independent of user activity)

- Azure Container Registry (image storage)
- Terraform state storage
- Base Container App environment overhead
- OIDC federation infrastructure

---

## Premium Tier Design Space

No premium tier is currently built. This is intentional — the team is validating core product mechanics before designing monetization.

The spark economy creates the natural design space:

- **Increased daily deck size**: currently a fixed deck per day. Premium users could receive a larger or refreshed deck.
- **Advanced voice and video features**: the SpeechBrain pipeline is in place. Premium could unlock longer voice notes, video capabilities, or deeper vocal affinity signals surfaced as product features.
- **Date planning tools**: Find Love generates 3 date ideas. Premium could expand to booking integration, planning tools, or calendar-aware scheduling.
- **Increased spark wallet ceiling**: currently max 10. Premium could raise or remove the ceiling.

None of these require fundamental architecture changes — they are configuration gates and UI additions on top of existing infrastructure.

What NOT to build as premium: ECHO score disclosure, community ratings, explicit AI explanation of why a match was shown. These violate the core design principle (invisible UX) and would damage trust even if users say they want it.

---

## What Not to Forecast

Do not put specific MRR, user count, ARPU, or churn rate projections in investor materials without beta data. Fabricated projections in a pre-revenue product undermine credibility with sophisticated investors.

Instead, present:
- The unit economics framework (what drives AI cost, what the $50/day cap means)
- The flywheel mechanics (what threshold unlocks personalization)
- The leading indicator framework (which signals predict retention)
- The infrastructure scaling model (what's linear vs. step-function)

Let investors model the numbers themselves, with the framework you provide. Their models will be more trusted than yours at this stage.

Placeholders:
- Trial continuation rate: [To be measured in beta]
- Find Love conversion rate: [To be measured in beta]
- Average ConnectionScores per user per week: [To be measured in beta]
- WeightLearningBatchWorker eligibility rate at week 4: [To be measured in beta]
- OpenAI DailyBudgetUsd utilization at [N] active users: [To be measured in beta]
- Average sparks consumed per active user per day: [To be measured in beta]
