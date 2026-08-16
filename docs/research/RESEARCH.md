# Woven — ECHO Design Research & Rationale

This document records the design reasoning behind ECHO's core decisions. Every claim is grounded in the actual behavior of the system as implemented — no speculation.

---

## 1. Why Behavioral Signals Instead of Stated Preferences

Woven does not ask users "what are you looking for?" and then match on the answers. It observes what users actually do — how fast they reply, whether they play voice notes to completion, whether they continue the trial — and uses those behaviors as the matching signal.

The reason: stated preferences are unreliable predictors of real compatibility. People self-report inaccurately — not from dishonesty, but because humans are poor at predicting what they will actually respond to. A user might say they want someone career-focused, but consistently engage faster with profiles that are playful and spontaneous. Stated preference says one thing; revealed preference (behavior) says another.

ECHO is built on revealed preferences. The signal ledger (`MatchSignalLogs`) captures behavioral events, not survey answers. The `WeightLearningService` learns from those events. The result is a matching model that reflects what a user actually responds to, not what they think they want.

---

## 2. Why Logistic Regression

`WeightLearningService` uses mini-batch logistic regression to update per-user scoring weights. The choice of logistic regression over more complex approaches (neural networks, gradient boosted trees) is deliberate:

- **Interpretable**: the learned weight vector is a list of coefficients — one per scoring component. It is possible to inspect which components matter most for a given user.
- **Sample-efficient**: logistic regression produces useful output with as few as `MinSamples = 10` labeled examples. A neural network requires thousands. Early users (who have few matches and few behavioral observations) still get personalized weights.
- **Avoids overfitting**: with small sample sizes, neural approaches would memorize noise. Logistic regression with appropriate regularization generalizes from limited data.

The `MinSamples = 10` threshold in `WeightLearningBatchWorker` reflects this: users with fewer than 10 `ConnectionScore` samples are skipped entirely — the global default weights are used instead. This prevents the learner from updating on statistically meaningless data.

---

## 3. Why Per-User Weight Learning

A single global weight vector — one set of weights applied to all users — cannot capture the heterogeneity of real attraction. One user might weight voice resonance heavily; another finds humor alignment the dominant signal; a third responds primarily to lifestyle compatibility.

`WeightLearningService` maintains a separate weight vector per user in the database. The `WeightLearningBatchWorker` runs every Sunday at 04:00 UTC and updates each eligible user's weights independently from their own behavioral history.

This means ECHO personalizes not just who it shows a user, but the criteria by which matches are ranked for that user. The 16-component `MatchScore` is the same for everyone; which of those 16 components matters most is different for everyone.

---

## 4. Why a Composite ConnectionScore Label

ECHO uses `ConnectionScore` — a 7-signal composite — as the training label for weight learning, rather than a binary "did they match / did they not" outcome.

Binary labels are high-noise. Two users might match but never exchange a single message. That is not a meaningful positive signal. Conversely, a trial that ended with `no_spark` after a genuine conversation is a more informative negative than a match that was never opened.

The 7-signal composite aggregates:
- `BalloonPop` (mutual interest)
- `TimeToFirstMessageMs` (speed of engagement)
- `ChatDepthMessages` (conversation depth)
- `TrialContinued` / `TrialEndedNoSpark` etc. (commitment decision)
- `DateIdeaAccepted` (willingness to meet)
- `MutualVoiceExchange` (voice note reciprocity)

This composite is a richer proxy for actual connection quality than any single signal. Weight learning against this label produces weights that optimize for genuine engagement, not just mutual likes.

---

## 5. Why MinConnectionScore = 0.08

The `WeightLearningService` filters out training samples where `ConnectionScore < MinConnectionScore = 0.08`.

A `BalloonPop` event alone scores approximately 0.05. Two users who mutually liked each other but never interacted — no messages, no trial — produce a `ConnectionScore` near this floor. This score is noise: it says "both users liked a profile photo" but nothing about actual compatibility.

By setting `MinConnectionScore = 0.08`, the learner only trains on pairs where at least some post-match behavior occurred. This means the weight vector learns from interactions with real signal content, not from like/dislike decisions made on minimal information.

---

## 6. Why 16 Scoring Components

`MatchScoringService` computes 16 distinct components for every (viewer, candidate) pair. The components cover:

- Embedding similarity dimensions (humor, lifestyle, style, voice, photo, emotional rhythm, visual preference)
- AI pillar scores (from `AiProfileService`)
- Behavioral fingerprint proximity
- Collaborative filtering signals (CfScore, when available)
- Recency and diversity adjustments

A single compatibility number loses information that is essential for personalization. If a user has learned weights that strongly favor humor alignment, the system needs the humor component as a separable dimension — it cannot be inferred from a combined score. The 16-component architecture ensures that `WeightLearningService` has fine-grained dimensions to learn from, and that per-user personalization can express real differences in what matters to each person.

---

## 7. Why the Behavioral Fingerprint Defaults to 0.5 (Neutral)

The behavioral fingerprint is a 16-dimensional vector computed from a user's behavioral history over a 180-day window. New users and users with sparse behavioral history have missing or incomplete fingerprint dimensions.

Missing dimensions default to `0.5`, not `0.0`.

This is the cold-start design choice. `0.0` would be interpreted by the scoring model as a strongly negative signal — "this user has no evidence of this behavioral trait." That is wrong: absence of evidence is not evidence of absence. A user who has not yet had enough matches to generate a behavioral fingerprint is unknown, not negative.

`0.5` is the neutral midpoint of the `[0, 1]` range used for all fingerprint dimensions. Defaulting to neutral means new users are treated as compatible with everyone on fingerprint dimensions where no data exists, which is the correct behavior until real signal accumulates.

---

## 8. Why LinUCB Bandit

`DeckSelectionService` uses a LinUCB contextual bandit (in addition to the learned scoring weights) to select the final daily deck.

Pure exploitation of learned preferences creates filter bubbles. If the system only shows candidates who score highest on the user's current weights, it reinforces existing patterns and never discovers whether the user might respond well to a different type of person. Over time, the model becomes increasingly confident in an increasingly narrow range.

LinUCB adds exploration: it deliberately selects some candidates with high uncertainty (not just high predicted score). The `alpha` parameter controls the explore/exploit balance — higher alpha means more exploration. This ensures the deck occasionally includes candidates the model is uncertain about, which provides the behavioral signal needed to keep the weight vector accurate over time.

---

## 9. Why a 180-Day Signal Window

`BehavioralFingerprintService` computes the fingerprint from signals in the last 180 days only. Older signals are excluded.

A 180-day window is long enough to capture seasonal patterns in behavior (summer vs. winter, busy vs. quiet periods) without being dominated by very old signals that no longer reflect the user's current preferences or life situation. People change. A fingerprint built from 3-year-old data would reflect who the user was, not who they are now.

180 days (approximately 6 months) was chosen to balance recency against signal volume — enough time for most active users to accumulate meaningful behavioral data.

---

## 10. Why Voice Embedding (ECAPA-TDNN, 192-dim)

`VoiceEmbeddingService` calls the SpeechBrain ECAPA-TDNN sidecar to generate a 192-dimensional voice embedding from each voice note.

Text-based embeddings capture what is said. Voice embeddings capture how it is said: speaking rate, pitch contour, rhythm, pause patterns, and energy — the prosodic features that carry emotional tone and personality information beyond word content.

Prosodic compatibility is real: people are more comfortable in conversations with partners whose speaking rhythm and energy level are compatible with theirs. This compatibility signal is unconscious — users cannot report it accurately in a survey. But it shows up consistently in behavioral outcomes (time to reply, trial continuation, mutual voice exchange).

ECAPA-TDNN is a speaker verification architecture pre-trained on large speech corpora. The 192-dim output represents a dense encoding of the speaker's voice characteristics. `VectorSearchService` can then compute cosine similarity between two users' voice embeddings as one of the 16 scoring components.

---

## 11. Why the Trial Period

The trial period is a 3-minute commitment gate. It begins when both users have opened the chat thread and ends with a deliberate decision: `CONTINUE`, `END`, or `BLOCK`.

Two reasons drove this design:

**Signal quality**: `TrialContinued` carries the highest single-event weight in the `ConnectionScore` composite (`0.25`). A user choosing to continue a trial after a 3-minute conversation has made a more informed, deliberate decision than a passive like. This deliberateness makes the signal substantially more informative than a profile swipe.

**Anti-ghosting**: most ghosting happens in the ambiguous period after a match but before any real conversation. The trial period creates a defined decision moment — both users know the clock is running and a decision is required. This reduces the social ambiguity that enables ghosting. The `no_spark` / `wrong_timing` / `not_my_type` end reasons are also fed into ECHO as granular negative signals, enabling finer-grained weight learning than a generic "ended" outcome.

---

## 12. Why Tone Personalization in Match Explanations

`MatchExplanationService` reads the user's `TimeToFirstMessageMs` history before generating the match explanation headline and bullets. Users who historically reply quickly when the explanation tone is playful receive playful explanations; users who reply quickly to earnest, direct tones receive earnest explanations.

The rationale: the same explanation content lands differently for different users. A witty, self-aware explanation about shared humor feels engaging to one user and performative to another. `TimeToFirstMessageMs` is a behavioral proxy for which tone preceded fast engagement for that specific user.

This is an example of the broader ECHO philosophy: user preferences are revealed through behavior, and the system adapts to revealed preferences rather than asking users to self-report what tone they prefer.
