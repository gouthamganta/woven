# Woven — Product Story

## What Woven Is

Woven is a dating app built around intentionality and behavioral matching. Every design decision — from the 5-response daily cap to the invisible AI — is in service of one idea: meaningful moments over swiping volume.

Most dating apps are built around a simple loop: swipe, match, ghost. They optimize for engagement time and swipe counts because those are the metrics advertisers and App Store rankings reward. The result is a product that makes users feel productive while producing very little — a treadmill of half-hearted swipes and conversations that trail off.

Woven rejects that loop entirely.

---

## The Core Premise

Woven is built on three convictions:

**1. Intentionality is a feature, not a friction.**
Every positive action in Woven requires a written opening note — 20 to 150 characters, composed before the choice is submitted. There is no such thing as a reflexive swipe that creates a match. If you choose someone, you have said something to them first.

**2. Behavioral signals reveal more than stated preferences.**
Users routinely say they want one thing and respond to another. Woven's ECHO matching engine ignores what users say they want in the abstract and pays attention to what they actually do — how fast they respond to someone, whether they listen to a voice note all the way through, how often they continue after a trial period, which date ideas they choose. These signals build a behavioral fingerprint that updates weekly and shapes what each user sees on their daily deck.

**3. AI should be invisible.**
Woven never shows users a compatibility score, a community rating, or a ranking. The "What caught our eye" section on every deck card explains why ECHO surfaced that person — in plain language, with a tone calibrated to the viewer — but never references a number. The matching engine is infrastructure, not a selling point.

---

## How Woven Looks to a User

When a user opens Woven, they land on **Moments** — their curated daily deck. Five profiles appear per day, chosen by ECHO from the full candidate pool. Each card shows the person's name, a verification badge if they've been verified, a short match explanation ("What caught our eye"), and the two choice buttons: ◈ Magical and ◇ Resonant.

**Magical (◈)** signals a full, instinctive emotional connection — the person feels right in a way you don't entirely analyze.

**Resonant (◇)** signals an intellectual or deeply aligned connection — the kind of match where you've read their profile and feel genuine recognition.

Before either choice is submitted, the user writes an opening note. The note is private — it is not shown to the other person until a mutual match exists. If both users have chosen positively and written their notes, a **Balloon** is created — the connection window — and both users see each other's notes for the first time.

There is no notification of a missed connection. If you chose someone and they haven't chosen you back yet, you see `RECORDED_WAITING`. The system does not say whether they've seen your card at all. The Drawn tab is the only window into who chose you first — and that view costs a spark.

---

## The Drawn Tab

The **Drawn** tab shows people who have already chosen the viewer positively in the last 7 days. These are real expressions of interest, not algorithmic suggestions. Responding to a Drawn entry costs 1 spark from the viewer's SparkWallet — not because Woven wants to monetize attention, but because the cost creates a decision. A Drawn response is a deliberate act.

If the viewer doesn't respond within the 7-day window, the entry expires.

---

## The Spark Economy

Sparks are the app's soft currency. Users receive 5 sparks per day and can hold a maximum of 10.

Sparks are spent on Drawn actions (1 spark each). They are partially refunded as a "ghost refund" (0.5 sparks to each user) when a match closes with no messages exchanged. The ghost refund exists to penalize passivity — if you matched and said nothing, some of the spark cost comes back, but not all.

There are no paywalls. No feature is locked behind payment. The spark economy is the only soft gate in the product.

---

## Matches (Balloons)

A match in Woven is called a **Balloon** — a connection window that lasts 7 days. Within the Balloon, two users share a chat thread.

Matches have two types:
- **PURE**: both users chose the same type (both Magical, or both Resonant)
- **EDGE**: the users chose different types (one Magical, one Resonant)

The match type is shown to both users. It is one of the few moments Woven acknowledges what kind of connection was signaled — without scoring it.

---

## The Trial Period

Some matches include a **Trial** — a 3-minute live window that starts when the second user opens the chat thread. During those 3 minutes, both users can message freely. When the trial ends, each user must make a decision:

- **CONTINUE** — I want to keep this conversation going
- **END** — this isn't right for me (with a reason: no spark, wrong timing, not my type)
- **BLOCK** — close and block immediately

If both users CONTINUE, the match proceeds and Find Love unlocks immediately. If either user ENDs, the match closes. The end reason is captured by ECHO as a behavioral signal — it informs future matching without being shown to either user.

The trial exists because Woven believes real attraction reveals itself in conversation faster than profile browsing. Three minutes of live messaging tells ECHO more than any profile field.

---

## Find Love

**Find Love** is the final stage of a match. It unlocks 5 minutes after both users have sent at least one message — a short reflection window to let the conversation breathe before the next step.

When it unlocks, 3 date ideas appear in the chat thread. These ideas are generated by ECHO using shared interests, the tone of the conversation, and prior choices from both users' histories. Each idea is concise (under 15 words), and covers distinct activity types — typically one active, one social, one casual.

Each user selects one idea. When both have chosen, a notification fires to both and venue suggestions unlock. Until both have chosen, each user sees "Waiting to see if they're in…"

The Find Love flow is the closest Woven comes to a structured ask: do you want to meet this person? The answer requires a positive action from both sides.

---

## Commons — The Content Layer

**Commons** is Woven's content feed. Users post tiles — photos, videos, text, or voice recordings — that appear in other users' feeds. Tiles expire (typically after 7 days).

Commons is not a social network. There are no follower counts, no likes shown, no public engagement metrics. The interaction that matters is the **Orbit** (◈ on a tile) — an explicit romantic interest signal sent to the tile owner. An Orbit generates OrbitGravity, which feeds directly into ECHO's scoring pipeline.

Commons is also where Woven's behavioral matching benefits from ambient data: how long a user dwells on a tile, whether they orbit it, whether they return to a profile after seeing a tile — all of this shapes ECHO's candidate scoring without the user ever being told.

---

## Games in Chat

Two AI-powered games can be played inside any chat thread:

**Know Me** — one user generates 3 questions about the other from their profile data. The other user answers and rates their own tendencies. Score = how well the guesser predicted the self-rating. Difficulty (EASY / MEDIUM / HARD) controls how guessable the questions are.

**Red Flag / Green Flag** — one user generates 3 statements about the other. The guesser rates each statement as green, yellow, red, or "depends." The target then self-rates. Score = alignment. A 90-second time limit keeps the pace live. The AI generates a 1–2 sentence post-game insight about the pair.

Both games are grounded in real profile data, not generic prompts. They exist to accelerate the conversational depth that trial periods and voice notes begin.

---

## Voice Notes

Users can record voice notes in chat (up to 180 seconds). Audio is uploaded directly to Azure Blob storage via a SAS token, then confirmed and sent as a VOICE message type.

When a recipient listens to a voice note all the way through, a `VoiceNoteListenComplete` signal is recorded by ECHO. If both users send voice notes in the same thread, a `MutualVoiceExchange` signal is recorded. These signals are among the strongest behavioral indicators of genuine interest — they represent effort and vulnerability that text does not.

---

## What ECHO Is (and Isn't)

ECHO is Woven's matching AI. It is a 16-component weighted scoring engine that runs silently behind every Deck curation decision. It learns per-user weights weekly using logistic regression on real connection outcomes. No two users see the same deck for the same reason after enough behavioral data accumulates.

ECHO is not a chatbot. It is not an assistant. It is not something users interact with. Its job is to surface the right person at the right time — and to get better at that job every week based on what actually worked.

The match explanation shown on every deck card ("What caught our eye") is ECHO's output, rendered in human language. It is honest — grounded in actual signals — but never numerical.

---

## What Woven Does Not Do

- Does not show raw compatibility scores
- Does not show community ratings or flag scores
- Does not show age on Moments cards
- Does not show who you've missed after they expire
- Does not show follower counts or public engagement on Commons tiles
- Does not apply hover animations that lift cards (design rule: hover = glow only)
- Does not lock features behind payment

---

## The Name

"Woven" references the act of weaving — individual threads (behavioral signals, pillar answers, behavioral fingerprints) brought together into something that holds. A match in Woven is not an accident of proximity or a swipe reflex. It is the result of two people choosing each other through multiple layers of intentional action: writing a note, starting a conversation, surviving a trial, selecting a date idea.

The name captures what the product believes: that a real connection is built from many small acts of attention, not a single tap.

---

*See also: [HIGH_LEVEL_DESIGN.md](HIGH_LEVEL_DESIGN.md) | [USER_LIFECYCLE.md](USER_LIFECYCLE.md) | [FEATURES.md](FEATURES.md)*
