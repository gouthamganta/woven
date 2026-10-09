# Candidate Pool Service

**System:** Matchmaking / ECHO  
**Related:** [ECHO README](../echo/README.md) | [Deck Selection](./deck-selection.md)

---

## Overview

`CandidatePoolService` builds the filtered pool of potential matches for a user, applying gender reciprocity, trust filtering, blocking, and age preferences.

---

## Filtering Rules

**Applied in SQL (efficient):**

1. **Gender Reciprocity**
   - User A seeks gender X → candidates must be gender X
   - Candidate must seek User A's gender
   - Evidence: [CandidatePoolService.cs](../../../backend/WovenBackend/Services/Matchmaking/CandidatePoolService.cs)

2. **Trust Threshold**
   - `trust_score >= 0.25`
   - Evidence: [TrustService.cs:9](../../../backend/WovenBackend/Services/Trust/TrustService.cs#L9)

3. **Blocking**
   - Exclude users in `blocks` table (both directions)

4. **Age Preferences**
   - Candidate age within user's `minAge`/`maxAge`
   - User age within candidate's preferences (reciprocal)

5. **Already Seen**
   - Exclude candidates shown in previous decks (`CandidateExposure`)

---

## Pool Size

**Typical pool:** 50-200 candidates (after filtering)

**Output:** List of candidate user IDs → passed to `MatchScoringService`

---

## Performance

**Optimization:** All filters done in single SQL query with indexes

**Indexes:**
- `users(gender, age, trust_score)`
- `blocks(blocker_user_id, blocked_user_id)`
- `candidate_exposure(user_id, candidate_id)`

---

## Related

- [Match Scoring Service](../echo/scoring.md)
- [Deck Selection](./deck-selection.md)
- [Trust System](../trust/README.md)
