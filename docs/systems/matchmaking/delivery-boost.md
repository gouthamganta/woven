# Delivery Boost System

**System:** Matchmaking / Trust  
**Related:** [Anti-Ghosting](../trust/anti-ghosting.md) | [Connection Scores](../echo/scoring.md)

---

## Overview

Users with high response rates and low ghosting behavior get priority placement in daily decks.

---

## Boost Factors

**Positive signals:**
- Fast first message response
- High chat depth (messages per match)
- Trial CONTINUE rate
- Low ghost rate (matches with 0 messages)

**Negative signals:**
- Ghosting (no response after match)
- Trial END immediately
- Low engagement

---

## How It Works

Delivery boost is a multiplier applied during deck selection:

**Formula:**
```
final_score = base_match_score × delivery_boost
```

**Boost range:** 0.8 - 1.2

**Evidence:** Calculated from `ConnectionScore` aggregated signals

---

## Impact

Users with high boost appear higher in other users' decks, increasing match likelihood.

---

## Related

- [Anti-Ghosting System](../trust/anti-ghosting.md)
- [Connection Scores](../echo/scoring.md)
- [Trust Score](../trust/trust-score.md)
