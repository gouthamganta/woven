# Woven MVP Execution Plan
**Date:** 2026-10-15  
**Status:** ACTIVE EXECUTION  
**Goal:** Ship investor-ready MVP in 8 weeks

---

## 🎯 THE PIVOT

**From:** Perfectionism loop (fix bugs → find more bugs → repeat forever)  
**To:** Ship-first mindset (fix blockers → strengthen core → launch → iterate)

**Why:** We've been stuck in QA for weeks. 80% of the app works. Time to ship.

---

## 📊 WHAT WE HAVE (CURRENT STATE)

### ✅ **Core Product (80% Complete):**
- Full authentication (Google OAuth, JWT)
- 9-step onboarding (basics → photos → preferences)
- Daily deck (Moments) with AI explanations
- Matching engine (14 scoring components)
- Chat (text + voice notes)
- Trial period + balloon lifecycle (72h)
- Commons (anonymous feed)
- ECHO AI pipeline (signal recording, nightly aggregation, weekly learning)
- Real-time messaging (SignalR + Redis)
- Profile management

### ⚠️ **Known Issues (Blockers):**
- #166: EmailHash lookup (mostly fixed, needs backfill)
- #165: Account deletion partial failure
- Auth guards commented out (5-line fix)
- #169: Landing page broken (24MB load, no privacy link)
- #181: Intent tag overlap
- #120: Afterthought rating not fully wired

### ❌ **Missing (Critical for MVP):**
- **Mini-games onboarding** (our marketing hook)
- **Photo/visual attraction learning** (ECHO differentiator)
- Several embedding workers not wired

---

## 🚀 MVP SCOPE DECISIONS

### **IN SCOPE (Must Ship):**

#### **1. Mini-Games Onboarding**
**Why:** Marketing hook + viral waitlist growth + engaging UX

**Approach:**
- **UI:** 6 simple Angular components (clean, tried-and-tested patterns)
- **No complex animations** — just interactive (click, drag, swipe)
- **Match existing app UX** — same design tokens, same feel
- **Content:** GPT-4 generates randomized questions per user
- **Mapping:** Simple scoring function (answers → 8 pillar scores)

**Games:**
1. **Energy Spectrum** → Energy pillar (slider, scenario picker)
2. **Value Auction** → Values/Stability (drag-and-drop ranking)
3. **Conversation Starter** → Communication/Curiosity (card swipe)
4. **Affection Language** → Affection (match gestures)
5. **Lifestyle Puzzle** → Lifestyle (tile selection)
6. **Ambition Ladder** → Ambition (goal ranking)

**Each game:**
- 3 difficulty levels (simple/mid/hard)
- Randomized questions from GPT-4 pool
- Progress indicator
- Results screen ("You're a [personality type]!")
- Shareable results (viral loop)

**Dev strategy:**
- Build reusable mechanics (one slider component used across games)
- GPT-4 generates questions dynamically (no hardcoding 100+ questions)
- Simple scoring (no ML, just weighted averages)
- Polish with Angular animations (built-in, no external libs)

**Timeline:** 1.5 weeks (down from 3 weeks with this approach)

---

#### **2. Photo/Visual Attraction Learning**
**Why:** ECHO's killer feature — learn physical preferences without asking

**Approach:**
- **Start simple:** Photo embeddings via CLIP (512-dim)
- **Attribute extraction:** Hair color, height estimate, skin tone, style
- **Track interactions:**
  - Tile views ≥8s → "what photos does user linger on?"
  - Orbit actions → "what content do they boost?"
  - ChatNote targets → "who do they pursue?"
  - ChatNote senders → "who pursues them?"
- **Build preference profile:**
  ```json
  {
    "attracted_to": {
      "hair_color": {"blonde": 0.7, "brown": 0.5},
      "height": {"short": 0.8, "tall": 0.2},
      "style": {"casual": 0.6, "athletic": 0.7}
    },
    "attracts": {
      // Who sends ChatNotes TO this user
    }
  }
  ```
- **Feed to ECHO:** New matching component #15 (PhotoSimilarity, weight 0.10)

**Implementation:**
- Wire PhotoEmbeddingService (CLIP model via Replicate/OpenAI)
- Create PhotoEmbeddingWorker (runs on upload)
- Track tile interactions → visual preferences
- Track ChatNote patterns → attraction analysis
- Add PhotoSimilarity to deck scoring

**Timeline:** 1 week

---

#### **3. Wire All Embeddings (1536-dim + Phase 3D)**
**Why:** Unlock full ECHO matching power

**Missing workers:**
- ChatNoteEmbeddingWorker → PreferenceEmbedding (what they find attractive, stated)
- TileViewProcessorWorker → ReceptionEmbedding (what they engage with, revealed)
- CfScoreBatchWorker → SharedTileAffinity (collaborative filtering)

**Phase 3D embeddings to implement:**
- IntentEmbedding (1536-dim) — relationship intent + openness tags
- StyleEmbedding (1536-dim) — communication style, tone, formality
- HumorEmbedding (1536-dim) — humor type from messages + game responses
- LifestyleEmbedding (1536-dim) — activity preferences, routines, energy patterns
- EmotionalRhythmEmbedding (1536-dim) — response cadence, emotional expression
- AttachmentProxyEmbedding (1536-dim) — attachment style from conversation patterns

**Timeline:** 1 week (simple ones first, then complex)

---

#### **4. Bug Fixes (Critical Path)**
1. Fix #166 (EmailHash backfill)
2. Fix #165 (Account deletion transaction)
3. Enable auth guards (5 lines)
4. Fix #169 (Landing page optimization)
5. Fix #181 (Intent tag overlap)
6. Fix #120 (Afterthought rating)

**Timeline:** 1 week

---

### **OUT OF SCOPE (Defer Post-Launch):**

- #130: Wingmate safety features (share date, SOS)
- #129: ID verification + liveness selfie
- #131: Live location for date ideas
- #128: Zodiac sign field
- #118: First Ten (10-min) + Last Call (3-min) balloon gates
- #134: Action-to-date funnel tracking
- #135: Your Weave viral game
- Swap feature (#122)
- Intro feature (#121)
- ECHO learning v2 (#127)

**These are "known issues / coming soon" — document, don't build.**

---

## 📅 8-WEEK EXECUTION TIMELINE

### **Week 1: Bug Fixes (Fast Track)**
- ✅ Fix #166 (EmailHash backfill)
- ✅ Fix #165 (Account deletion)
- ✅ Enable auth guards
- ✅ Fix #169 (Landing page)
- ✅ Fix #181 (Intent tags)
- ✅ Fix #120 (Afterthought)

**Deliverable:** All critical bugs closed, app stable

---

### **Week 2: Photo Learning Foundation**
- ✅ Wire PhotoEmbeddingService (CLIP integration)
- ✅ Create PhotoEmbeddingWorker
- ✅ Build attribute extraction pipeline
- ✅ Add PhotoSimilarity matching component

**Deliverable:** Photo embeddings working, not yet learning from interactions

---

### **Week 3: Visual Preference Learning**
- ✅ Track tile photo interactions
- ✅ Track ChatNote target patterns
- ✅ Build preference profile aggregation
- ✅ Wire to ECHO matching

**Deliverable:** ECHO learns visual preferences from behavior

---

### **Week 4: Wire All Embeddings**
- ✅ ChatNoteEmbeddingWorker (PreferenceEmbedding)
- ✅ TileViewProcessorWorker (ReceptionEmbedding)
- ✅ CfScoreBatchWorker (SharedTileAffinity)
- ✅ Test full ECHO pipeline end-to-end

**Deliverable:** All 15 matching components wired and working

---

### **Weeks 5-6: Mini-Games (UI + Backend)**
- ✅ Design 6 game UI components (wireframes)
- ✅ Build reusable mechanics (slider, drag-drop, card-swipe, ranking)
- ✅ Wire GPT-4 question generation
- ✅ Build scoring logic (answers → 8 pillars)
- ✅ Create shareable results screens

**Deliverable:** 6 mini-games fully playable

---

### **Week 7: Integration + Polish**
- ✅ Replace foundational questions with mini-games in onboarding
- ✅ Wire mini-game results to ECHO
- ✅ Build shareable results (viral marketing)
- ✅ Polish animations + micro-interactions
- ✅ Test mini-games with 5 real users

**Deliverable:** Onboarding = engaging game experience

---

### **Week 8: Beta Testing + Launch Prep**
- ✅ 10-person closed beta (real humans, not synthetic)
- ✅ Fix critical bugs discovered
- ✅ Deploy to production (Azure)
- ✅ Launch waitlist with mini-games
- ✅ Monitor first 100 users

**Deliverable:** Live app, real users, waitlist growing

---

## 🎮 MINI-GAME DEVELOPMENT STRATEGY

### **Philosophy:**
- **Simple > Complex** — One mechanic per game, executed well
- **Interactive > Animated** — Clicks/drags feel good, no gratuitous effects
- **Consistent > Creative** — Match existing app UX, don't reinvent
- **Generated > Hardcoded** — GPT-4 creates content, we build containers

### **Tech Stack:**
- **UI:** Angular components (existing patterns)
- **Animations:** Angular animations (built-in, no external libs)
- **Content:** GPT-4 API (question generation)
- **Storage:** Existing onboarding API endpoints
- **Scoring:** TypeScript functions (simple math)

### **Reusable Components:**

```typescript
// Shared mechanic components
SliderGameComponent      // Energy Spectrum, others
DragDropRankingComponent // Value Auction, Ambition Ladder
CardSwipeComponent       // Conversation Starter
MatchPairsComponent      // Affection Language
TileSelectionComponent   // Lifestyle Puzzle

// Each game = wrapper + config
EnergySpectrumGame {
  mechanic: SliderGameComponent,
  questions: generateQuestions("energy"),
  scoring: (answers) => energyPillarScore(answers)
}
```

### **Question Generation (GPT-4):**

```typescript
// Backend endpoint: POST /games/generate-questions
{
  "pillar": "energy",
  "level": "simple",
  "count": 5
}

// Returns:
{
  "questions": [
    {
      "id": "q1",
      "text": "Friday night. Your phone pings with two invites: house party or cozy movie night. Which pulls you?",
      "options": [
        {"key": "party", "label": "Party", "score": 0.8},
        {"key": "movie", "label": "Movie", "score": 0.3}
      ]
    },
    // ... 4 more randomized questions
  ]
}
```

**Advantages:**
- No hardcoded questions (infinite variety)
- Each user gets different experience
- Easy to A/B test question styles
- Questions evolve without code changes

### **Scoring Logic:**

```typescript
// Simple weighted average
function calculatePillarScore(answers: Answer[]): number {
  const total = answers.reduce((sum, a) => sum + a.score, 0);
  return total / answers.length; // Returns [0, 1]
}

// All 6 games → 8 pillar scores
{
  "Lifestyle": 0.78,
  "Energy": 0.62,
  "Values": 0.85,
  "Communication": 0.55,
  "Ambition": 0.43,
  "Stability": 0.70,
  "Curiosity": 0.66,
  "Affection": 0.58
}
```

### **Shareable Results (Viral Loop):**

```
[Card/Image]
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
I'm a Curious Explorer! 🌟

Energy: High 🔥
Values: Depth-first 🎯
Curiosity: Always learning 📚

Find your match on Woven
→ wooven.me/play
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

Users share results → friends play mini-games → conversion to waitlist.

---

## 📸 PHOTO LEARNING TECHNICAL APPROACH

### **Phase 1: Embedding (Week 2)**

```
User uploads photo →
  Strip EXIF (privacy) →
  Send to CLIP model (OpenAI/Replicate) →
  Get 512-dim embedding →
  Store in photo_embeddings table
```

**CLIP Model:** `openai/clip-vit-base-patch32` (fast, free tier available)

### **Phase 2: Attribute Extraction (Week 2)**

```python
# Pseudo-code for attribute extraction
def extract_attributes(photo_url):
  # Use CLIP's zero-shot classification
  hair_colors = ["blonde", "brown", "black", "red", "gray"]
  hair_result = clip.classify(photo, hair_colors)
  
  styles = ["casual", "formal", "athletic", "boho"]
  style_result = clip.classify(photo, styles)
  
  # Height from full-body shots (heuristic)
  has_full_body = detect_full_body(photo)
  height_estimate = estimate_height(photo) if has_full_body else None
  
  return {
    "hair_color": hair_result.top_label,
    "style": style_result.top_label,
    "height_class": height_estimate
  }
```

**Store per photo:**
```json
{
  "photo_id": 123,
  "user_id": 456,
  "embedding": [0.12, -0.34, 0.56, ...], // 512-dim
  "attributes": {
    "hair_color": "blonde",
    "style": "casual",
    "height_class": "average"
  }
}
```

### **Phase 3: Preference Learning (Week 3)**

**Track interactions:**
```sql
-- User views tile ≥8s
INSERT INTO visual_preference_signals (
  viewer_id, target_photo_id, signal_type, strength
)
VALUES (123, 789, 'TileDwell', 0.6);

-- User orbits tile
INSERT INTO visual_preference_signals (
  viewer_id, target_photo_id, signal_type, strength
)
VALUES (123, 789, 'Orbit', 0.8);

-- User sends ChatNote to someone
INSERT INTO visual_preference_signals (
  viewer_id, target_user_id, signal_type, strength
)
VALUES (123, 456, 'ChatNoteSent', 1.0);
```

**Aggregate preferences (nightly worker):**
```sql
-- For user 123, what photo attributes do they engage with?
SELECT 
  pe.hair_color,
  AVG(vps.strength) as preference_score
FROM visual_preference_signals vps
JOIN photo_embeddings pe ON vps.target_photo_id = pe.photo_id
WHERE vps.viewer_id = 123
  AND vps.created_at >= NOW() - INTERVAL '90 days'
GROUP BY pe.hair_color;

-- Result:
-- { "blonde": 0.75, "brown": 0.45, "black": 0.30 }
```

**Store in UserVector:**
```json
{
  "visual_preferences": {
    "hair_color": {"blonde": 0.75, "brown": 0.45},
    "style": {"casual": 0.65, "athletic": 0.70},
    "height": {"short": 0.80, "average": 0.40}
  },
  "attracted_by": {
    // Who sends ChatNotes TO this user
    "hair_color": {...},
    "style": {...}
  }
}
```

### **Phase 4: Matching Integration (Week 3)**

**New component: PhotoSimilarity (weight 0.10)**

```csharp
// MatchScoringService.cs
private async Task<float> ComputePhotoSimilarityAsync(
    int viewerId, 
    int candidateId,
    CancellationToken ct)
{
    var viewerPrefs = await GetVisualPreferences(viewerId, ct);
    var candidateAttrs = await GetPhotoAttributes(candidateId, ct);
    
    // Simple attribute match
    float score = 0f;
    
    // Hair color match
    if (viewerPrefs.HairColor.TryGetValue(candidateAttrs.HairColor, out var hairPref))
        score += hairPref * 0.4f;
    
    // Style match
    if (viewerPrefs.Style.TryGetValue(candidateAttrs.Style, out var stylePref))
        score += stylePref * 0.3f;
    
    // Height match (if available)
    if (viewerPrefs.Height.TryGetValue(candidateAttrs.HeightClass, out var heightPref))
        score += heightPref * 0.3f;
    
    return Math.Min(1f, score);
}
```

**Deck scoring now includes visual attraction:**
```
Total Score = 
  PillarCosine (0.22) +
  IntentAlignment (0.18) +
  LifestyleCompatibility (0.12) +
  ConnectionScore (0.15) +
  ... +
  PhotoSimilarity (0.10)  ← NEW
```

---

## 🧬 EMBEDDING WORKERS (WIRE ALL)

### **1. ChatNoteEmbeddingWorker**
- **Schedule:** Every 4 hours
- **Input:** Last 20 ChatNotes user wrote
- **Output:** PreferenceEmbedding (1536-dim)
- **Purpose:** "What does this user find attractive?" (stated)

```csharp
// Worker logic
foreach (var user in eligibleUsers) {
  var notes = await GetLastChatNotes(user.Id, count: 20);
  var combinedText = string.Join("\n", notes.Select(n => n.Text));
  var embedding = await _openAi.EmbedAsync(combinedText);
  
  await UpdateUserVector(user.Id, 
    preferenceEmbedding: embedding);
}
```

### **2. TileViewProcessorWorker**
- **Schedule:** Every 30 minutes
- **Input:** Last 50 tiles user dwelled on (≥8s)
- **Output:** ReceptionEmbedding (1536-dim)
- **Purpose:** "What content does this user engage with?" (revealed)

```csharp
foreach (var user in activeUsers) {
  var viewedTiles = await GetDwelledTiles(user.Id, 
    minDwellSeconds: 8, count: 50);
  
  var embeddings = viewedTiles.Select(t => t.ContentEmbedding);
  var meanEmbedding = ComputeMean(embeddings);
  
  await UpdateUserVector(user.Id, 
    receptionEmbedding: meanEmbedding);
}
```

### **3. CfScoreBatchWorker**
- **Schedule:** Daily 05:00 UTC
- **Input:** Orbit + dwell interactions
- **Output:** CfScores table (Jaccard similarity)
- **Purpose:** "Users who liked X also liked Y" (collaborative filtering)

```csharp
// Already implemented, just needs to be scheduled
services.AddHostedService<CfScoreBatchWorker>();
```

---

## ✅ SUCCESS CRITERIA

### **Week 4 Checkpoint:**
- [ ] All 6 bug fixes closed
- [ ] Photo embeddings working
- [ ] Visual preference learning active
- [ ] All 15 ECHO components wired
- [ ] Backend test coverage ≥27% (current baseline)

### **Week 6 Checkpoint:**
- [ ] Mini-games playable
- [ ] GPT-4 question generation working
- [ ] Shareable results generated
- [ ] Onboarding completion rate ≥70%

### **Week 8 Launch:**
- [ ] 10 beta users completed full flow
- [ ] ≥50 waitlist signups (organic)
- [ ] Zero P0 bugs
- [ ] ECHO generating quality matches (verified by users)
- [ ] Photo learning showing measurable impact

---

## 🎯 WHAT WINNING LOOKS LIKE

**4 weeks from now:**
- App is stable (no critical bugs)
- ECHO is learning from photos + text + behavior
- 15/15 matching components working

**8 weeks from now:**
- Mini-games live on landing page
- Users sharing results ("I'm a Curious Explorer!")
- 100+ waitlist signups
- Ready for investor demo

**12 weeks from now (post-launch):**
- 500+ users
- Matches leading to actual conversations
- Data proving ECHO works (time-to-first-message, trial acceptance)
- Investor pitch deck backed by real metrics

---

## 📋 DECISIONS MADE

### **✅ IN:**
- Mini-games (marketing + onboarding)
- Photo learning (visual attraction)
- All embeddings wired
- Bug fixes (critical path)

### **❌ OUT (Post-Launch):**
- Safety features (Wingmate)
- ID verification
- Live location
- Balloon gates refinement
- Action-to-date tracking

### **🤔 PRINCIPLES:**
- **Ship > Perfect** — 80% working beats 100% theoretical
- **Simple > Complex** — Clean UI beats fancy animations
- **Generated > Hardcoded** — AI creates content, we build containers
- **Behavior > Questions** — Learn from what users do, not what they say

---

## 🚢 LET'S SHIP

**Next steps:**
1. Post this plan to GitHub
2. Start bug fixes (Week 1)
3. Daily standups (async updates)
4. Ship every Friday (weekly demos)

**End goal:** Investor-ready MVP in 8 weeks.

Let's go. 🚀

---

**Document Version:** 1.0  
**Last Updated:** 2026-10-15  
**Owner:** Founder + Claude (ECHO co-founder)  
**Next Review:** Weekly (every Friday)
