# Woven Product Rules v2 — Source of Truth

**Owner:** Product (Claude PO session) · **Approved by:** Founder, 2026-10-08
**Status:** Active. This file overrides `PRODUCT_STORY.md`, `docs/business/rules.md`
and `qa/PRODUCT_RULES_INDEX.md` wherever they disagree. Rules marked
**PROPOSED** still need founder sign-off.

Each rule lists the decision, what the code does today (verified at `3df9759`),
and the gap.

---

## 0. Names (founder, 2026-10-08)

| Concept | Name | Visual |
|---|---|---|
| A match | **Thread** | Existing balloon (unchanged) |
| 10-min window after two-way | **First Ten** | Existing balloon timer |
| Pop action | **Pop** | Existing balloon pop |
| 3-min trial | **Last Call** | — |
| Find Love | **Knot** | "You two tied a Knot" |
| Private close rating | **Afterthought** | — |
| Waitlist game | **Woven** | — |

## 1. Matching

| ID | Rule | Code today | Gap |
|---|---|---|---|
| M-1 | To like someone you must choose ◈ Magical or ◇ Resonant **and** write a ChatNote (20–150 chars). | `MomentsEndpoints /choose` requires note 20–150. | None |
| M-2 | Match forms when both have chosen + written a note. | Balloon created only when both response + note exist. | None |
| M-3 | Same choice → **Pure match**. Different choice → **Edge match** with an edge owner. | `MatchType`, `EdgeOwnerId`. | None |
| M-4 | Both ChatNotes are pinned at the top of the chat when it opens. | `chat-thread.component.html` renders notes. | QA to verify |

## 2. Profile visibility

| ID | Rule | Code today | Gap |
|---|---|---|---|
| V-1 | Pure match: both see full profiles immediately. | `/profile-access` → FULL. | None |
| V-2 | Edge match: only the edge owner sees the full profile right after match. | EDGE_OWNER → FULL; other → LIMITED. | None |
| V-3 | When two-way conversation starts, both see full profiles. | Unlocks on `BothMessagedAt`. | None |

"Two-way conversation" = each person has sent at least one message.

## 3. Balloon lifecycle

```
Match ──(no two-way within 72h)──► EXPIRED (closed)
  │
  └─ two-way starts ─► FIRST TEN (10 min, profiles fully open)
                          │
                          ├─ nobody pops in 10 min ─► FIND LOVE (no timers)
                          │
                          └─ someone pops ─► LAST CALL (3 min trial)
                                                │
                                                ├─ both CONTINUE ─► FIND LOVE
                                                └─ anything else ─► CLOSED
```

| ID | Rule | Code today | Gap |
|---|---|---|---|
| B-1 | Balloon expires **72h** after match if two-way conversation never starts. | `MomentsRules.BalloonLifetime = 36h`. | **Change to 72h** (#107) |
| B-2 | Once two-way starts, expiry no longer applies. | Expiry worker filters `BothMessagedAt == null`. | None |
| B-3 | Two-way start opens **First Ten**: a 10-minute window. | `ReflectionWindow = 5 min`. | **Change to 10 min** |
| B-4 | Either person can pop during First Ten → starts **Last Call** (3-min trial). | Pop rejected with `CANNOT_POP_NOW` once `FindLoveAt` is set, which happens at two-way start. **Pop is impossible inside the window.** | **Bug — fix** |
| B-5 | Last Call timer starts when both have the thread open after the pop. | Implemented. | None |
| B-6 | Last Call decisions: CONTINUE / END (reason) / BLOCK. Both CONTINUE → Find Love. | Implemented. | None |
| B-7 | Last Call **always ends at 3 min**. Only both CONTINUE → Find Love; one/no decision, one-sided texting or END → CLOSED (`no_decision`). | Resolved only when someone next opens the thread; no worker. | **Add worker** |
| B-8 | Nobody pops in First Ten → automatic Find Love. | Find Love shows when `FindLoveAt <= now`. | Retime to 10 min |
| B-9 | CLOSED is final. Expired/ended ≠ blocked. | Implemented. | None |

Superseded from the old story: the "1-minute grace window after pop" and
"popper chooses end or request to continue". Last Call replaces both.

## 4. Afterthought — private rating on every close

| ID | Rule | Code today | Gap |
|---|---|---|---|
| R-1 | Whenever an active match closes **for any reason**, each person gets a private Afterthought prompt. | Rating only on `/unmatch`. | **Extend** |
| R-2 | Afterthoughts are **never shown to any user**, in any form (PO decision 2026-10-08: visible ratings cause retaliation, popularity lock-in and harm women who reject). Optional later: private, opt-in coaching tips with no numbers. | Deck cards show a red/green rating bar when count ≥ 5 (`MomentsEndpoints.cs:208`, `moments.page.html:99`). | **Bug — remove** |
| R-3 | Afterthoughts feed ECHO as a *pair outcome label*, plus a separate safety channel. They never act as a public reputation score. | Used only by insights. | Build |

PROPOSED per close path:

| Close path | Who is asked | Asked what |
|---|---|---|
| Last Call END / trial timeout | Both | 1–5 + one tag (no spark, timing, not my type, felt off) |
| Unmatch from Find Love | Both | 1–5 + tag |
| 72h expiry (never talked) | Both, lightweight | "What got in the way?" (busy, not feeling it, forgot, other) — no star rating |
| BLOCK | Blocker only | Safety reason (feeds trust & safety only, **never** matching) |
| Blocked user | Not asked | — |

## 5. Unchanged rules (from current docs, verified in code)

- **Deck:** 5 profiles/day (2 Balanced, 2 Explorer, 1 Boost), built 03:30 UTC.
- **Saved is removed** (founder 2026-10-08) and replaced by Drawn. `DailyPendingCap`
  in `InteractionBudgetService` is a leftover. Remove it or confirm Drawn uses it.
- **Sparks:** new wallet 5.0; +5.0 earned lazily the first time you use the app
  each UTC day; cap 10.0; Drawn action costs 1; ghost refund 0.5 when a match
  closes with no messages. Stored as tenths. Old story says "reset at midnight".
  The code tops up and caps rather than resetting.
- **Trust:** candidates below 0.25 trust are excluded; blocks are excluded both ways.
- **Games:** Know Me and Red/Green Flag, max 2 per day per match.
- **Date idea:** unlocks after 10 minutes in Find Love.
- **No paywalls, no ages on deck cards, no community ratings, both ChatNotes are
  visible to the matched pair in chat and also inform background matching signals.**

## 5b. Launch & platform decisions (founder, 2026-10-08)

- Launch city: **Hyderabad**. Market: India.
- Paid services (OpenAI, Replicate, TTS, KYC vendors) **allowed for testing**, within a budget cap TBD.
- Matrimony mode comes later, but filters and data model must be matrimony-ready now.

## 6. Proposed features (needs founder approval)

- **Intro** — hold a deck card for a personal introduction. See the Issue.
- **Swap** — swap a deck card for 1 Spark, 2/day. See the Issue.
- **Getting to Know You** — redesigned foundational step. See the Issue.
