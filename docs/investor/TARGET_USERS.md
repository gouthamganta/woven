# Target Users

## Primary User Profile

Woven targets intentional daters in the 22-35 age range who have prior experience with mainstream dating apps and have arrived at a specific type of frustration: not that they can't get matches, but that matches don't lead anywhere.

This user has tried Tinder. They have likely tried Hinge. They have a reasonable number of matches but a low number of meaningful conversations, and an even lower number of dates that felt worth having. They describe their relationship with dating apps as "exhausting" or "like a second job." They are still using one or more apps because they haven't found a better alternative, not because the apps are working.

---

## Behavioral Characteristics

The target user is characterized by a specific willingness to invest:

**Willing to write notes.** Woven requires a 20-150 character note when making a choice on a Moments card. This is a deliberate friction point. The user Woven is designed to attract does not experience this as annoying — they experience it as meaningful. They have something to say about why they're interested, and they prefer a platform that asks.

**Willing to play games.** KnowMeAgent (preference discovery game) and RedGreenFlagAgent (values alignment game) are accessible within a match. The target user is willing to engage in structured conversation games because they prefer it to the unstructured blank-slate opening message that most apps provide. Games reduce the anxiety of first contact by providing a shared context.

**Willing to send voice notes.** The target user is comfortable with voice as a communication medium in digital contexts. They understand that a 30-second voice note conveys more than three paragraphs of text, and they are willing to record one. This is not universal behavior — it is characteristic of a user segment that values authentic expression over performance-optimized text.

**Willing to engage in the trial period.** The 3-minute mutual trial window asks users to make an active commitment decision after a match opens. The target user does not experience this as pressure — they experience it as a signal of mutual seriousness. They prefer a platform that creates this checkpoint to one where ghosting is the default post-match behavior.

---

## What This Engagement Creates: The Flywheel

The target user profile is not just a market segment description. It is a training data quality description.

Each intentional behavior generates a higher-quality signal than its low-effort equivalent:

| Behavior | Signal generated | ECHO use |
|---|---|---|
| Writing a ChatNote | Timestamped reasoning at decision moment | PreferenceEmbedding (planned) |
| Playing KnowMeAgent | Preference discovery in structured conversation | Pillar and style signal refinement |
| Sending a voice note | Audio content + engagement pattern | VoiceEmbedding (192-dim), MutualVoiceExchange |
| Completing trial period | Explicit commitment decision | TrialAccepted (0.25 ConnectionScore weight) |
| Accepting a date idea | Revealed meeting intent | DateAccepted (0.15 ConnectionScore weight) |
| Writing a ChatNote on a Moments pass | Reasoning for non-selection | Negative preference signal |

Users who engage more generate richer behavioral data. Richer behavioral data produces more accurate ConnectionScore labels. More accurate labels produce better per-user weight adaptation. Better weight adaptation produces a more accurate daily deck. A more accurate daily deck produces better matches. Better matches produce more engagement.

This is the flywheel. It requires users who engage intentionally to function. The spark economy and effort gates (notes, trial period) filter the user base toward exactly this type of user.

Users who are unwilling to write notes, unwilling to engage in the trial period, or unwilling to invest in conversation will find Woven's design friction higher than alternatives. This is a feature, not a bug — their absence from the platform protects the signal quality for users who do engage.

---

## Secondary Characteristics

**Prior app fatigue, not app abandonment.** The target user has not given up on dating apps conceptually. They have given up on the specific mechanics (infinite swipe, low-commitment match, ghost). They are looking for an app that works differently, not for an app that works harder at the same thing.

**Prefers fewer, better options.** Consumer research on dating app satisfaction consistently shows this preference in the 22-35 educated-professional segment. The finite daily deck (Moments) is designed for this user. They prefer curated to comprehensive.

**Privacy-aware.** The target user is aware that dating apps collect behavioral data and is increasingly skeptical of how it is used. Woven's AES-256-GCM encryption on PII fields, private blob storage for media, and explicit design rule against showing users their own behavioral data or others' signals is positioned toward this concern.

**Willing to meet, not just match.** The target user's frustration is not with matching — it is with the gap between matching and meeting. Find Love (date idea generation) and the trial period mechanic are both designed for this: they reduce the activation energy required to move from in-app conversation to real-world meeting.

---

## What Woven Is Not Targeting

Understanding the anti-target is as important as understanding the target.

**Casual hookup seekers** who want high-volume, low-commitment matching will find Woven's spark economy, note requirement, and trial period unnecessarily constrictive. Tinder is a better product for them. Woven is not designed to compete in that segment.

**Users who want infinite matches** as a validation mechanism — the user who opens the app to feel desired, not to meet someone — will find the finite daily deck and soft effort gates unsatisfying. This is intentional.

**Users who reject any effort investment.** Woven's friction is designed to filter for intentional engagement. A user who will not write a 20-character note and will not engage in a 3-minute trial period will not generate the behavioral signal quality that ECHO requires to function. Including them does not improve the product — it degrades signal quality for users who do engage.

**Users outside the 22-35 age range** are not explicitly excluded, but the product design (games, voice notes, curated deck, trial period) reflects the communication preferences and frustration patterns of this cohort specifically. Woven has not designed for the 45+ re-entry market or the 18-21 casual exploration market.

---

## Market Sizing Context

The total number of single adults in the US in the 22-35 age range is in the tens of millions. Widely reported dating app usage statistics indicate that a majority of single adults in this age range have used at least one dating app. The addressable subset — users who are actively frustrated with current options and willing to invest effort in a better alternative — is a fraction of this, but remains a large and commercially meaningful segment.

Woven is not attempting to capture all dating app users. It is attempting to be the definitive product for intentional daters — a segment that is currently underserved, growing in self-identification, and highly valuable as a training data source for behavioral ML.
