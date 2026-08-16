# Market Dynamics

## The Dating App Market: Size and Structure

The global dating app market is widely reported to exceed $10 billion in annual revenue, with projections showing continued growth through the decade. The top three platforms — Tinder, Hinge, and Bumble — account for the majority of global market share and collectively define the current interaction paradigm: photo-first profiles, swipe-based discovery, and preference questionnaires as the primary matching input.

This concentration is not a sign of a solved market. It is a sign of a market locked into a design pattern that was pioneered in 2012 and has not fundamentally changed since.

---

## User Behavior Trends

### Swipe Fatigue

Multiple consumer surveys documented over the past several years report a consistent finding: the majority of active dating app users describe their experience as frustrating, exhausting, or anxiety-producing. The mechanics that drove initial growth — infinite scroll, gamified swipe, match count as a social signal — are now the primary sources of user dissatisfaction.

Key reported symptoms:
- High match-to-conversation conversion failure (matches that never result in a single message)
- Ghosting as a normalized behavior, particularly after short conversations
- Users cycling between apps, returning when frustrated, leaving again shortly after
- A perception that the apps optimize for continued engagement, not for actual outcomes

### Desire for Intentionality

The same surveys show users expressing a desire for fewer, better matches rather than more matches. Concepts like "quality over quantity" and "I want someone who actually wants to meet" appear consistently in reported user sentiment. A meaningful subset of users — primarily in the 22-35 age range — report willingness to invest effort into matching if that effort produces measurably better conversations and connections.

This is the behavioral shift Woven is designed to serve.

### Privacy and Data Concerns

Awareness of how dating apps use personal data has grown substantially. Users increasingly question what happens to the data they provide — photos, stated preferences, behavioral patterns — and whether platforms use it in their interest or against it. Regulatory pressure (GDPR, state-level US privacy laws) has elevated this concern from a fringe issue to a mainstream consideration.

Woven's architecture addresses this directly: AES-256-GCM encryption on all PII fields (email, full name, city, state, reflection sentences), private Azure Blob Storage containers with SAS token access for all media, and a design principle that behavioral signals are used to improve matches — not sold, not shown back to users as metrics.

---

## Why Behavioral ML Represents the Next Evolution

The dominant matching paradigm relies on stated preferences: users declare what they want (height, education, distance, dealbreakers) and the algorithm filters accordingly. Decades of behavioral economics research, and a growing body of evidence from dating app studies, confirm the well-known gap between stated and revealed preferences. People consistently choose partners whose profiles they would have filtered out, and avoid partners whose profiles they would have selected.

Questionnaire-based systems have a structural ceiling: they are only as accurate as users' self-knowledge, which is limited by definition.

Behavioral ML bypasses this ceiling by observing what users actually do — which matches they open, how quickly they respond, whether they send voice notes, how deeply conversations develop, whether they agree to meet — and learning from those revealed preferences over time. The signal is behavior, not belief.

Woven's ECHO pipeline is built on this foundation. Rather than asking users what they want and filtering accordingly, ECHO scores candidates across 16 behavioral and embedding-based components, learns per-user adaptive weights from a 7-signal composite outcome label (ConnectionScore), and reorders the daily deck using a contextual bandit (LinUCB) that balances exploitation of known preferences with exploration of candidates the user has not yet seen.

The result is a matching engine that gets more accurate as users engage — not a static filter applied at onboarding.

---

## What the Current Market Leaders Get Wrong

### Stated Preferences as Ground Truth

Hinge's "Dealbreakers" and compatibility percentage displays, Bumble's filter stack, and Tinder's Elo-based sorting all treat stated preferences as reliable signal. They are not. Users who claim to want someone within 10 miles consistently message and meet people who are further away. Users who mark certain traits as dealbreakers consistently swipe on profiles that violate those declarations.

Building a matching engine on stated preferences produces a system that is accurate at matching profiles to declarations but poor at predicting actual connection outcomes.

### Infinite Scroll and Gamification

The infinite scroll model optimizes for session length, not for matching outcomes. A user who swipes for 30 minutes and gets 5 matches generates more revenue-correlated engagement signals than a user who spends 5 minutes and finds one meaningful conversation. The incentive structure is misaligned with user intent.

Gamification compounds this: when match counts, super-likes, and daily streaks become the psychological hooks, users are retained by game mechanics rather than by value delivered. The gap between what feels rewarding in-app and what produces a real date grows wider.

### Showing Metrics Users Didn't Ask For

Displaying compatibility percentages (Hinge), rose counts (Hinge's premium feature), Spotlight impressions, and similar metrics turns the matching process into a performance where users optimize for platform-visible signals rather than for genuine connection. Users begin crafting profiles to maximize compatibility scores rather than to represent themselves accurately. The system trains on corrupted signal.

Woven's design rule is explicit: no compatibility scores, no community ratings, no AI labels or badges are ever shown to users. Every AI surface is invisible UX. Users experience better matches, not a dashboard of how they are being evaluated.

---

## The White Space Woven Is Entering

The premium-intentional quadrant of the dating app market is structurally undercapitalized. There is no app currently occupying the intersection of:

- Curated daily deck (not infinite scroll)
- Behavioral ML that adapts per user (not static filters)
- Invisible AI (matching as infrastructure, not as a visible feature)
- Soft effort gate (sparks, notes, games) that filters for intentional users
- Voice and conversational depth signals embedded in the matching model

Thursday is the closest in spirit (intentionality, limited daily access) but operates as an events-only once-a-week mechanic with no underlying behavioral ML. Coffee Meets Bagel offers curated delivery but without adaptive scoring. Hinge positions as "designed to be deleted" but its matching model is preference-questionnaire-driven and it shows compatibility percentages.

Woven has built the technical infrastructure to occupy this quadrant — the ECHO pipeline, the 9-modality embedding system, the per-user weight learning, and the behavioral signal ledger (MatchSignalLogs) that accumulates the training data needed to make per-user adaptation meaningful over time.

The white space is real. The infrastructure to address it is built.
