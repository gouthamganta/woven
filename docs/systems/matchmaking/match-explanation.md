# Match Explanation Service

**System:** Matchmaking / ECHO  
**Related:** [Match Explanations](../../features/matches/explanations.md) | [Bridge Questions](../../features/matches/bridge-questions.md)

---

## Overview

Generates natural-language explanations for why two users were matched, using AI to identify common ground from their profiles.

---

## How It Works

**Input:**
- User A profile (foundational answers, vectors)
- User B profile
- Match score components

**Process:**
1. Extract shared interests/values
2. Identify bridge question (common ground)
3. Generate 2-sentence explanation via OpenAI

**Output:**
- `explanationText` — shown on match card
- `bridgeQuestion` — conversation starter

---

## Evidence

[MatchExplanationService.cs](../../../backend/WovenBackend/Services/Matchmaking/MatchExplanationService.cs)

---

## Example

**Explanation:**  
"You both value deep conversations over small talk and enjoy exploring new perspectives."

**Bridge Question:**  
"What's a topic you could talk about for hours?"

---

## Related

- [Match Explanations Feature](../../features/matches/explanations.md)
- [Bridge Questions](../../features/matches/bridge-questions.md)
- [Match Narrator](../../features/matches/narration.md)
