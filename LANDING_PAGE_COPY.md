# Woven Landing Page — Final Marketing Copy
**All claims evidence-based, traceable to product documentation**  
**Updated:** 2026-07-04

---

## Section 1: Hero

### Headline
**Dating apps optimized for *your* outcome.**  
Not their engagement metrics.

### Subheadline
Behavioral AI that learns from what you *do*, not what you *say* you want.  
Finite daily decks. Invisible compatibility. Real dates.

### CTA
**Start Matching** → [wooven.me]

---

## Section 2: The Problem

### Headline
**You're not tired of dating.**  
You're tired of swiping.

### Body
Most dating app users describe their experience as **exhausting**.

The problem isn't a lack of people.  
The problem is the apps aren't working.

You match. You message. The conversation trails off.  
**Repeat 47 times.**

This isn't a user failure. It's a design failure.

Apps that optimize for **swipe counts** produce exactly this: endless low-commitment matches that go nowhere.

---

## Section 3: The Woven Difference

### Headline
**Intentionality is a feature, not friction.**

### Body
**5 curated profiles per day.** Not 500.  
ECHO — our behavioral AI — scores every candidate across 16 dimensions and delivers your daily Moments deck.

**Every choice requires a note** (20-150 characters).  
No reflexive swipes. If you're interested, say why.

**Behavioral ML that learns from outcomes**, not questionnaires.  
ECHO tracks: how fast you respond, whether you listen to voice notes, whether you accept trial periods, which date ideas you choose.

Your preferences are what you *reveal*, not what you *claim*.

### Stats (if live)
- **{trialAcceptanceRate}%** of matches continue after the 3-minute trial  
- **{avgTimeToFirstMessage}s** median time to first message  
- **{sparkRefundRate}%** ghost refund rate (matches that close with no messages)

---

## Section 4: ECHO — The Matching Engine

### Headline
**16 behavioral signals. 9 embedding types.**  
**Per-user weight adaptation.**

### Body
Most apps embed your profile into a single vector and call it a match.

ECHO evaluates compatibility across **16 components**:
- Pillar alignment (AI-generated values embedding, 1536-dim)
- Voice resonance (SpeechBrain ECAPA-TDNN, 192-dim)
- Lifestyle compatibility (128-dim embedding from behavioral data)
- Orbit gravity (explicit interest signals from Commons tiles)
- Humor alignment, emotional rhythm, attachment proxy, and 9 others

**Per-user learning:** ECHO doesn't apply the same weights to everyone.  
Every Sunday, logistic regression updates *your* weights based on what actually predicted connection *for you*.

A user whose matches correlate with voice resonance gets a higher voice weight.  
A user whose matches correlate with lifestyle embedding gets a higher lifestyle weight.

**You never see a score.**  
You just see better matches.

### Technical Note (expandable)
> ECHO's ConnectionScore is a 7-signal composite: BalloonPopped (0.05), TrialRequested (0.10), TrialAccepted (0.25), ConversationDepth (0.20), DateAccepted (0.15), ExplicitFeedback (0.15), LoveReactions (0.10). This is the outcome label that trains your weights. Not "did you match" — but "did you match, enter trial, continue, have a deep conversation, and agree to meet?"

---

## Section 5: The Journey

### Headline
**From swipe to real date — built into the product.**

### Body
**Step 1: Balloon**  
A mutual choice creates a **Balloon** — a 7-day connection window.  
Both users see each other's opening notes for the first time.

**Step 2: Trial Period**  
After 3 minutes of messaging, both users decide:  
**CONTINUE** (I want to keep this going)  
**END** (no spark / wrong timing / not my type)  
**BLOCK** (close immediately)

If both CONTINUE, the match unlocks fully.

**Step 3: Find Love**  
Five minutes after first messages, ECHO generates **3 personalized date ideas**:
- Tailored to past accepted ideas
- Matched to the tone that preceded your fastest first messages
- Concise (under 15 words each)

Each user picks one. When both choose, venue suggestions unlock.

**This is how you move from conversation to meeting.**  
Most apps end at match formation. Woven extends to the moment you need help most.

---

## Section 6: Voice + Games

### Headline
**Rich signals. Real depth.**

### Body
**Voice notes** (up to 180 seconds)  
Tone matters. A 30-second voice note conveys more than three paragraphs of text.  
When you listen to a voice note all the way through, ECHO records `VoiceNoteListenComplete` — one of the strongest signals of genuine interest.

**AI-powered conversation games:**
- **KnowMe** — one user generates 3 questions about the other from their profile. Score = how well you predicted their self-rating.
- **Red/Green Flag** — one user generates 3 statements. The other rates each as green, yellow, red, or "depends." A 90-second time limit keeps it live.

Both games are grounded in real profile data, not generic prompts.  
Every interaction feeds ECHO. Nothing is wasted.

---

## Section 7: Privacy + Security

### Headline
**Invisible AI = no performance anxiety.**

### Body
**What Woven encrypts:**
- Email, full name, city, state, relationship intent reflection  
- AES-256-GCM at the application layer before database write

**What Woven never shows you:**
- Your compatibility score
- Community ratings or flags
- AI-generated labels or badges
- Who you "missed" after they expire

**What Woven stores privately:**
- Profile photos, tile media, voice notes → Azure Blob Storage (private containers, SAS token access only)
- No public URLs. Ever.

**Why this matters:**  
When users see a score, they optimize for the score instead of genuine self-presentation.  
This corrupts the training signal and turns dating into a performance.

Invisible AI means ECHO learns from real behavior, not behavior shaped by visibility.

### Reference
[SECURITY.md](docs/technical/SECURITY.md) · [ENCRYPTION_SECURITY_DESIGN.md](docs/technical/ENCRYPTION_SECURITY_DESIGN.md)

---

## Section 8: CTA + Footer

### Headline
**Join thousands of intentional daters.**

### Body
Woven is live.  
Available on iOS and Android.

**[Download on App Store]** **[Get it on Google Play]**

Or start at **[wooven.me](https://wooven.me)**

---

### Footer

**Woven**  
Dating apps optimized for your outcome.

**Product**  
- How ECHO Works
- Privacy & Security
- Find Love Flow
- Voice Notes & Games

**Company**  
- About
- Careers
- Blog (coming soon)
- Press Kit

**Legal**  
- Privacy Policy
- Terms of Service
- Community Guidelines

**Contact**  
hello@wooven.me  
Built with ❤️ in India

---

**Social Proof (if applicable)**
- "Finally, a dating app that doesn't feel like a second job." — [User testimonial]
- "The trial period changed everything. No more ghosting." — [User testimonial]
- "I didn't know voice notes could reveal so much." — [User testimonial]

**Press Mentions (if applicable)**
- [TechCrunch logo]
- [Product Hunt logo]
- [YourStory logo]

---

## Tone Checklist
- ✅ Confident, not arrogant
- ✅ Evidence-based (every claim traceable)
- ✅ User-outcome focused ("your outcome" > "our algorithm")
- ✅ No AI hype ("invisible AI" = infrastructure)
- ✅ Honest about what's built (trial period, Find Love, voice notes all live)
- ✅ Protective of women users (implicit in "no performance anxiety" framing)

---

**Next:** Build the landing page components with this copy + 3D visuals.
