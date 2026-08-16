# Competitive Landscape

## Overview

The dating app market is dominated by three platforms — Tinder, Hinge, and Bumble — that collectively define the current paradigm. Below is a factual description of each platform's core design decisions, followed by an analysis of how Woven's design decisions differ. This document does not claim Woven is superior; it describes the specific choices that make it a distinct product.

---

## Platform-by-Platform Analysis

### Tinder

Tinder pioneered swipe-based discovery and remains the highest-volume dating app globally by widely reported estimates. Its core mechanic is binary: right swipe (interest) or left swipe (pass) on photo-first profiles. Mutual right swipes unlock messaging.

Key design decisions:
- **Infinite scroll** — no limit on daily swipes in base tier
- **Photo-first, profile-second** — the swipe decision is made before reading any profile text
- **Elo-style desirability scoring** — candidates are ranked and delivered based on aggregate swipe history across the user base
- **Gamification** — Super Likes, Boosts, Gold/Platinum tiers create in-app purchase pressure tied to visibility mechanics
- **No behavioral ML post-match** — matching logic is based on desirability ranking and geo-proximity, not on predicted conversation or meeting probability
- **No outcome feedback loop** — matches that result in dates vs. matches that ghost are not distinguishable signals in the base product

Tinder's model optimizes for engagement volume. The incentive structure rewards continued swiping, not successful dates.

### Hinge

Hinge repositioned around the tagline "designed to be deleted" and introduced profile prompts (short-answer questions) as a primary signal alongside photos. Users can "like" specific parts of a profile (a photo or a prompt answer) with an optional comment, rather than binary swiping.

Key design decisions:
- **Preference questionnaires as matching input** — users declare dealbreakers (height, education, religion, ethnicity, distance) that filter the candidate pool
- **Compatibility percentage display** — Hinge shows users a compatibility score on match profiles, making the AI layer visible and turning it into a metric users optimize against
- **Roses** — a premium currency that sends a "priority like," creating a visible status signal within the app
- **Most Compatible** — an algorithmic daily recommendation, but based on declared preference alignment rather than behavioral outcomes
- **No trial period mechanic** — once matched, there is no structured commitment window; ghosting is the default post-match behavior
- **No voice signal** — no voice note embedding or voice resonance as a compatibility dimension

Hinge is the closest competitor in positioning (intentional, relationship-focused) but its matching model is grounded in stated preferences, and its compatibility score display contradicts its "invisible AI" potential.

### Bumble

Bumble's core differentiator is a mechanic: after a mutual match, only the woman can send the first message, and must do so within 24 hours or the match expires. This was designed to reduce unsolicited contact from men and to give women more control over the initiation dynamic.

Key design decisions:
- **Same underlying matching model** — the candidate pool and ordering are photo-first and preference-filter-based, similar to Tinder and Hinge
- **24-hour initiation window** — creates urgency but also pressure; the expiration mechanic is a soft commitment gate, though not as structured as Woven's trial period
- **BFF and Bizz modes** — Bumble has expanded beyond dating, diluting the core use case
- **No behavioral ML post-match** — matching is not informed by conversation quality, voice note exchange, or date acceptance signals
- **No spark economy** — matching is free-for-all within the preference filter stack

Bumble's feminist-first mechanic addresses a real UX problem (unwanted first contact) but does not change the underlying matching model or its reliance on stated preferences.

### Coffee Meets Bagel (CMB)

CMB introduced curated delivery to the mainstream market: rather than infinite scroll, users receive a limited number of curated candidates per day ("bagels"). This design decision directly addresses swipe fatigue by creating intentional constraints.

Key design decisions:
- **Daily limited delivery** — curated, not infinite
- **"Beans" currency** — a soft gate mechanism similar in spirit to Woven's spark economy
- **No behavioral ML** — curation is based on stated preferences and mutual connection graph, not behavioral outcome signals
- **No trial period, no games, no voice** — the matching flow ends at mutual like; no structured conversational scaffolding
- **No per-user adaptive weights** — curation algorithm is not personalized through behavioral outcome learning

CMB is the closest precedent for Woven's delivery model (curated daily deck) but without the behavioral ML layer that makes Woven's deck ordering adaptive over time.

### Thursday

Thursday operates on a once-a-week mechanic: the app is only active on Thursdays, and users must meet in person that day. The model is events-driven and intentionality-first by design constraint rather than by behavioral infrastructure.

Key design decisions:
- **Weekly access window** — intentionality enforced by scarcity of access
- **No persistent matching model** — there is no ML pipeline adapting to user behavior over time
- **No voice, no games, no behavioral fingerprint** — the match flow is minimal; intentionality comes from the temporal constraint, not from product depth
- **Geography-limited** — the model works in dense urban markets with sufficient weekly active users; it does not scale to lower-density markets

Thursday represents a design philosophy aligned with intentionality but without the technical infrastructure to sustain it as a product that improves over time.

---

## Woven's Differentiated Design Decisions

The following describes specific product and technical decisions Woven has made that are not present in any of the platforms above. These are design decisions, not claims of superiority.

### 1. Invisible Behavioral ML

ECHO's 16-component scoring system is never surfaced to users. There are no compatibility percentages, no AI labels, no badges. Users experience the output (a curated deck, personalized explanation tone, contextually relevant date ideas) without seeing the mechanism. This is a deliberate design choice: showing compatibility scores trains users to optimize for the score rather than for the relationship.

### 2. Curated Daily Deck (not infinite scroll)

Woven delivers a finite daily deck of candidates — Moments — rather than an infinite scroll. This mirrors CMB's intentionality constraint but is backed by a behavioral ML pipeline that makes deck composition adaptive per user over time.

### 3. Trial Period Mechanic

When a match opens the chat thread, a structured 3-minute mutual connection window begins. Both users must signal intent to continue before the connection fully unlocks (Find Love). This creates a commitment signal (TrialAccepted) that feeds directly into ECHO's ConnectionScore — it is simultaneously a UX decision and a training data decision. No comparable mechanic exists in Tinder, Hinge, Bumble, CMB, or Thursday.

### 4. Spark Economy (soft gate, not paywall)

Woven uses sparks — 5 per day, 1 per Drawn action, 0.5 ghost refund when a match ends with no messages — as a behavioral gate. The economy rewards engagement and penalizes low-commitment matching behavior. There is no hard paywall; sparks are the gate. This is distinct from Tinder's Boost/SuperLike premium currency model (which buys visibility, not engagement quality) and from CMB's Beans (which function similarly but without the ghost refund fairness mechanism).

### 5. Per-User Adaptive Weights

ECHO's 16 scoring components carry base weights, but those weights are adjusted per user via mini-batch logistic regression (lr=0.01, 100 iterations, L2 regularization) running every Sunday 04:00 UTC. A user whose connection outcomes are strongly predicted by voice resonance will have a higher voice weight than a user whose outcomes are driven by lifestyle embedding similarity. No competing platform has published a comparable per-user adaptive weight system.

### 6. Voice and Games in Match Flow

Voice notes (SpeechBrain ECAPA-TDNN, 192-dim embedding) and in-match games (KnowMeAgent for preference discovery, RedGreenFlagAgent for values alignment) are embedded in the post-match flow and generate behavioral signals that feed ECHO. These are not social features bolted on for engagement; they are training data surfaces.

### 7. ChatNotes as Background Signal

Woven requires users to write a note (20-150 characters) when making a choice on a Moments card. These notes are never shown to other users or surfaced as feedback. They exist as behavioral signal — the act of writing the note, and its content, informs future embedding generation. No competing platform has a comparable invisible note-as-signal mechanic.

---

## Differentiation Summary Matrix

| Dimension | Tinder | Hinge | Bumble | CMB | Thursday | Woven |
|---|---|---|---|---|---|---|
| Delivery model | Infinite scroll | Infinite scroll | Infinite scroll | Daily curated | Weekly window | Daily curated deck |
| Matching input | Swipe history + geo | Stated preferences | Stated preferences | Stated preferences | N/A | Behavioral outcomes |
| Per-user adaptive weights | No | No | No | No | No | Yes (logistic regression) |
| Compatibility score shown | No | Yes | No | No | No | No (invisible AI) |
| Trial period mechanic | No | No | No | No | No | Yes (3-minute window) |
| Soft effort gate | No | No | No | Beans (partial) | Temporal | Spark economy |
| Voice signal in matching | No | No | No | No | No | Yes (SpeechBrain embedding) |
| In-match games | No | No | No | No | No | Yes (KnowMeAgent, RedGreenFlagAgent) |
| Note as background signal | No | No | No | No | No | Yes (ChatNotes) |
| Date idea generation | No | No | No | No | No | Yes (Find Love, 3 AI ideas) |
