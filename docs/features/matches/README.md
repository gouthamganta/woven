# Matches

**What is a match?** Two users who both expressed interest in each other (both chose MAGICAL or RESONANT on the Deck tab). A match creates a "balloon" — a connection window with specific access rules and lifecycle states.

---

## Core concepts

| Concept | What it means |
|---|---|
| **Match** | Mutual interest between two users. Created when both users express intent (MAGICAL ◈ or RESONANT ◇ choice). |
| **Balloon** | The connection window. State machine: ACTIVE → CLOSED. |
| **Pure match** | Both users made the SAME choice (both ◈ or both ◇). Full profile access immediately. |
| **Edge match** | Users made DIFFERENT choices (one ◈, one ◇). Asymmetric access rules. |
| **Edge owner** | In an Edge match, the user who chose MAGICAL (◈) owns the edge. Gets full profile access immediately. |
| **Trial period** | 3-minute decision window after balloon is popped. Both users decide: CONTINUE, END, or BLOCK. |
| **Find Love** | Final unlock stage. Happens after both users message each other. Unlocks date ideas and deeper features. |

---

## Match lifecycle

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Mutual interest detected                                     │
│    → Match record created (ACTIVE balloon)                      │
│    → BalloonState: ACTIVE, ExpiresAt: now + 96h                 │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│ 2. User pops balloon (POST /matches/{id}/pop)                   │
│    → Trial starts (3 minutes)                                   │
│    → TrialStartedAt set, TrialEndsAt = now + 3min              │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│ 3. Trial decision (POST /chats/{threadId}/trial-decision)       │
│    → Both users choose: CONTINUE / END / BLOCK                  │
│    → If either blocks: ClosedReason = BLOCK                     │
│    → If either ends: match continues but marked                 │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│ 4. Both users message each other                                │
│    → BothMessagedAt timestamp set                               │
│    → FindLoveAt = BothMessagedAt + 12 hours (reflection period) │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│ 5. Find Love unlocks (when now >= FindLoveAt)                   │
│    → Date ideas revealed                                        │
│    → Full profile unlocked (if EDGE non-owner)                  │
│    → Date coordination begins                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Match creation

**Where matches come from:**
- `POST /moments/respond` with `action: "MAGICAL"` or `action: "RESONANT"`
- Backend checks if the candidate also chose the viewer
- If mutual interest → `Match` record created via `MatchService.CreateMatchAsync()`

**Match type determination:**
```csharp
if (userAChoice == userBChoice)
{
    matchType = MatchType.PURE;
    edgeOwnerId = null;  // no edge owner in pure matches
}
else
{
    matchType = MatchType.EDGE;
    // Edge owner = whoever chose MAGICAL
    edgeOwnerId = (userAChoice == "MAGICAL") ? userAId : userBId;
}
```

---

## Balloon states

| State | Meaning |
|---|---|
| **ACTIVE** | Match is live. Users can chat, pop balloon, unmatch. |
| **CLOSED** | Match ended. Immutable. No further actions allowed. |

**Closed reasons:**
- `POP` — one user popped the balloon and trial ended
- `EXPIRE` — balloon expired after 96 hours without being popped
- `UNMATCH` — user clicked unmatch (soft close, no negative signal)
- `BLOCK` — user blocked the other (safety feature, creates Block record)

---

## Access levels

When a user views a match profile (`GET /matches/{matchId}/profile`), the backend returns one of two access levels:

| Access Level | Who gets it | What they see |
|---|---|---|
| **FULL** | • Pure match (both users)<br>• Edge match owner<br>• Edge non-owner after 2-way messaging | All photos, full bio, all public optional fields, full intent |
| **LIMITED** | • Edge non-owner before 2-way messaging | 1 photo only, no bio, no optional fields, intent visible |

**Access unlock trigger for Edge non-owners:**
```csharp
var unlocked = match.BothMessagedAt != null;
```

---

## Match explanation

Every match includes an AI-generated explanation called "What caught our eye" — shown on the Moments card before the match is popped.

**Components:**
- **Headline** — 1 sentence (max 15 words), references aligned traits or shared interests
- **Bullets** — 1-2 bullet points (max 20 words each), mention specific shared data
- **Tone** — playful, calm, or serious (determined by user's pulse vector)
- **Bridge question** — Opening question pre-loaded into chat after match is created

**See:** [explanations.md](./explanations.md) for full details on generation logic.

---

## Bridge questions

**What:** A personalized opening question generated by ECHO, specific to this match pair.

**Where it appears:**
- In the match explanation on Deck tab
- Pre-loaded as the first suggested message in chat thread

**Requirements:**
- Rooted in real match data (shared tag, aligned pillar, hobby)
- Invites a story or memory (not yes/no)
- Max 20 words
- Sounds natural and warm

**Example:**
> "What's something you believed strongly that you've since changed your mind about?"

**See:** [bridge-questions.md](./bridge-questions.md)

---

## Voice narration

Matches on the Deck tab include optional voice narration — a TTS audio clip that reads a curated quote from the candidate's profile.

**Components:**
- **Ken Burns photos** — up to 3 profile photos in sort order
- **Curated quote** — shortest text ≥20 chars from bio or foundational answers (max 120 chars)
- **Narration URL** — OpenAI TTS (model: tts-1, voice: nova), cached in Azure Blob

**TTS generation:**
- Pre-generated at deck build time (5 parallel calls via Task.WhenAll)
- Cached at `tts/{candidateId}/{yyyyMMdd}.mp3` with 48h TTL
- If TTS fails, NarrationUrl is null — frontend silently skips audio

**See:** [narration.md](./narration.md)

---

## Trial period

**What:** A 3-minute decision window that starts when a user pops the balloon.

**How it works:**
1. User A pops balloon → `TrialStartedAt` set
2. Trial timer starts when BOTH users open the chat thread
3. `TrialEndsAt = now + 3 minutes` (set when both timestamps exist)
4. Both users decide: CONTINUE / END / BLOCK
5. If either blocks → match closes (ClosedReason = BLOCK)
6. If either ends → match continues but marked (for future features)

**Trial end reasons:**
- `no_spark` — didn't feel a connection
- `wrong_timing` — life circumstances
- `not_my_type` — preference mismatch

**Data flow:**
```
Match.TrialUserAOpenedAt → set when user A opens chat during trial
Match.TrialUserBOpenedAt → set when user B opens chat during trial
Match.TrialEndsAt        → set when both timestamps are non-null
Match.UserADecision      → "CONTINUE" | "END" | "BLOCK"
Match.UserBDecision      → "CONTINUE" | "END" | "BLOCK"
Match.TrialEndReason     → "no_spark" | "wrong_timing" | "not_my_type"
```

---

## Related features

- **[Pure vs Edge matches](./pure-vs-edge.md)** — detailed match type logic
- **[Explanations](./explanations.md)** — "What caught our eye" generation
- **[Bridge questions](./bridge-questions.md)** — opening question mechanics
- **[Narration](./narration.md)** — TTS voice narration
- **[Frontend implementation](./frontend.md)** — match-profile-preview.page.ts
- **[Backend implementation](./backend.md)** — Match entity, services, endpoints

---

## Database schema

**Table:** `matches`

| Column | Type | Description |
|---|---|---|
| `id` | uuid | Primary key |
| `user_a_id` | int | First user in the match |
| `user_b_id` | int | Second user in the match |
| `match_type` | enum | PURE (1) or EDGE (2) |
| `edge_owner_id` | int? | User who chose MAGICAL in EDGE match (null for PURE) |
| `balloon_state` | enum | ACTIVE (1) or CLOSED (2) |
| `closed_reason` | enum? | POP, EXPIRE, UNMATCH, BLOCK |
| `created_at` | timestamptz | Match creation timestamp |
| `expires_at` | timestamptz | Balloon expiration (96h after creation) |
| `closed_at` | timestamptz? | When balloon closed |
| `both_messaged_at` | timestamptz? | When both users sent at least 1 message |
| `find_love_at` | timestamptz? | When Find Love unlocks (BothMessagedAt + 12h) |
| `is_trial` | bool | Whether this match has trial mechanics enabled |
| `trial_started_at` | timestamptz? | When balloon was popped |
| `trial_ends_at` | timestamptz? | TrialStartedAt + 3 minutes |
| `user_a_decision` | varchar(20)? | CONTINUE, END, or BLOCK |
| `user_b_decision` | varchar(20)? | CONTINUE, END, or BLOCK |
| `trial_user_a_opened_at` | timestamptz? | When user A opened chat during trial |
| `trial_user_b_opened_at` | timestamptz? | When user B opened chat during trial |
| `trial_end_reason` | varchar(30)? | no_spark, wrong_timing, not_my_type |
| `date_idea_interested_a` | bool | User A clicked "I'm interested" on date idea |
| `date_idea_interested_b` | bool | User B clicked "I'm interested" on date idea |
| `date_idea_interested_at` | timestamptz? | When first user expressed interest |
| `date_agreed_at` | timestamptz? | When both agreed on date idea |

---

## API endpoints

**GET /matches**
- Returns all ACTIVE balloons for current user
- Includes other user's name, photo, verified badge
- Shows balloon timer, Find Love status

**GET /matches/{matchId}/profile-access**
- Returns access level (FULL or LIMITED) with reason
- Does NOT return profile data (lightweight check)

**GET /matches/{matchId}/profile**
- Returns full match profile with access-controlled data
- FULL: all photos, bio, optional fields
- LIMITED: 1 photo, no bio, no optional fields

**POST /matches/{matchId}/pop**
- Pops the balloon, starts trial period
- Idempotency-protected (X-Idempotency-Key header)
- Returns trial_started confirmation

**POST /matches/{matchId}/unmatch**
- Soft close (ClosedReason = UNMATCH)
- Optional rating parameter (1-5)

**POST /matches/{matchId}/block**
- Hard close (ClosedReason = BLOCK)
- Creates Block record (prevents future matching)

**POST /matches/{matchId}/flag**
- Safety report (reason: no_issues, uncomfortable, inappropriate)
- Does NOT close match (safety team reviews)

---

## Signal tracking

All match events feed into ECHO's preference learning pipeline via `MatchSignalLogs`.

**Key signals:**
- `TimeToFirstMessageMs` — speed of first message (primary outcome proxy)
- `ChatDepthMessages` — total message count
- `TrialContinued` / `TrialEndedNoSpark` — trial decision + reason
- `DateIdeaAccepted` — which date idea chosen
- `VoiceNoteListenComplete` — voice note played to end (0.05 weight)
- `MutualVoiceExchange` — both users sent voice notes (0.08 weight)

**See:** `MatchSignalService.RecordAsync()` for full event catalog.

---

## Hard rules

**No age on Moments cards.** Name + badge + explanation + actions only. Age is visible in profile view after match.

**No community ratings shown to users.** Platform-only signals. Users never see match scores or compatibility percentages.

**Ghost refund on unmatch.** If match ends with no messages exchanged, 0.5 sparks refunded to the user who spent them.

**Trial decisions are private.** Neither user sees the other's decision unless both choose to share verbally.

**Balloon expiration is 96 hours.** After that, balloon closes automatically (ClosedReason = EXPIRE).
