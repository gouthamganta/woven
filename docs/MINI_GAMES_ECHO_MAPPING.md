# Mini-Games → ECHO Pillar Mapping

## Overview
The 8 mini-games each map to one of the 8 AI Profile pillars in ECHO (the matching algorithm). Each game type is designed to capture specific behavioral signals that feed into the pillar scores.

---

## Game → Pillar Mapping

### 1. **Energy** (Slider Game)
**Question:** "After a long week, you recharge by..."
- **Left (0):** Netflix & chill alone
- **Right (100):** Party with friends

**Maps to:** Energy pillar (introvert ↔ extrovert spectrum)
**Data captured:** `energy_score` (0-100)

---

### 2. **Values** (Ranking Game)
**Question:** "If you could only keep 5, what matters most?"
- Items: Honesty, Adventure, Security, Growth, Deep connections

**Maps to:** Values pillar
**Data captured:** Ranked list with weights (1st = 5pts, 2nd = 4pts, etc.)

---

### 3. **Communication** (Swipe/Rating Game)
**Question:** "Rate how much these conversation topics excite you"
- Options: 5 fun conversation starters
- Rating: Strongly Disagree (1) → Strongly Agree (5)

**Maps to:** Communication pillar
**Data captured:** Array of `{option, rating}` pairs
**Processing:** Average rating + topic preferences

---

### 4. **Affection** (Selection Game)
**Question:** "How do you show someone you care?"
- Pick 2 from: Sweet texts, Quality time, Thoughtful gifts, Acts of service, Physical touch

**Maps to:** Affection pillar (Love Languages)
**Data captured:** Selected values array

---

### 5. **Lifestyle** (Selection Game)
**Question:** "Your perfect Saturday includes..."
- Pick 3 from: 6 lifestyle options

**Maps to:** Lifestyle pillar
**Data captured:** Selected values array
**Processing:** Activity preference clustering

---

### 6. **Ambition** (Ranking Game)
**Question:** "Rank what drives you most"
- Items: Career success, Meaningful relationships, Personal growth, Health & fitness, Financial freedom

**Maps to:** Ambition pillar
**Data captured:** Ranked priorities

---

### 7. **Stability** (Slider Game)
**Question:** "When making weekend plans..."
- **Left (0):** Whatever happens, happens
- **Right (100):** Color-coded calendar

**Maps to:** Stability pillar (spontaneity ↔ structure)
**Data captured:** `stability_score` (0-100)

---

### 8. **Curiosity** (Swipe/Rating Game)
**Question:** "How much do these things fascinate you?"
- Options: 5 curiosity triggers
- Rating: Strongly Disagree (1) → Strongly Agree (5)

**Maps to:** Curiosity pillar
**Data captured:** Array of `{option, rating}` pairs
**Processing:** Breadth (how many 4-5 ratings) + depth (max rating)

---

## Backend Processing

### Endpoint
`POST /onboarding/games/submit`

### Request Body
```json
{
  "answers": [
    {
      "gameId": "energy-spectrum",
      "pillar": "Energy",
      "answer": { "questionIndex": 0, "value": 65 }
    },
    {
      "gameId": "conversation-vibe",
      "pillar": "Communication",
      "answer": {
        "questionIndex": 0,
        "responses": [
          { "option": "Why pineapple on pizza is actually good", "rating": 5 },
          { "option": "Dream startup ideas", "rating": 4 }
        ]
      }
    }
  ]
}
```

### Processing Logic

1. **Extract raw scores** from each game answer
2. **Normalize to 0-100 scale** per pillar
3. **Store in `UserProfile.AiProfile`** (8 pillar scores + metadata)
4. **Generate embeddings** for matching via `EmbeddingService`

### Score Calculation Examples

**Slider games (Energy, Stability):**
```csharp
pillarScore = sliderValue; // Already 0-100
```

**Ranking games (Values, Ambition):**
```csharp
// Weighted average based on ranking position
foreach (item in rankedItems) {
    score += GetItemWeight(item) * (5 - position);
}
pillarScore = NormalizeToScale(score, 0, maxPossibleScore);
```

**Rating games (Communication, Curiosity):**
```csharp
// Average rating scaled to 0-100
avgRating = responses.Average(r => r.rating); // 1-5
pillarScore = (avgRating - 1) * 25; // Maps 1-5 to 0-100
```

**Selection games (Affection, Lifestyle):**
```csharp
// Categorical → composite score based on option weights
score = selectedValues.Sum(v => GetOptionWeight(v));
pillarScore = NormalizeToScale(score, 0, maxPossibleScore);
```

---

## Results Page (To Be Built)

### Design
After completing all 8 games, show a results page with:

1. **Animated reveal** of each pillar score
2. **Visual representation** (radial chart or bars)
3. **Short explanation** of what each score means
4. **Editable fields** - Let users add notes or adjust scores
5. **Continue button** → Next onboarding step

### Example UI
```
╔═══════════════════════════════════════╗
║  Here's what we learned about you     ║
╠═══════════════════════════════════════╣
║  Energy          ████████░░ 75%       ║
║  You recharge with people             ║
║                                       ║
║  Values          ██████████ 90%       ║
║  Growth & connection matter most      ║
║                                       ║
║  [... 6 more pillars ...]             ║
║                                       ║
║  Want to add anything?                ║
║  [Free text field]                    ║
║                                       ║
║  [ Continue to Photos ]               ║
╚═══════════════════════════════════════╝
```

---

## Future Enhancements

1. **Question randomization** - Shuffle options within games
2. **Adaptive difficulty** - Ask follow-up questions on extreme scores
3. **Fun facts** - Show "80% of users picked Adventure in top 3"
4. **Retry option** - Let users retake a specific game
5. **Export results** - Download PDF of their profile
