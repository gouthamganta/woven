# Daily Deck Budget

**Feature:** Moments daily limits  
**Related:** [Moments README](./README.md) | [Deck Tab](./deck-tab.md)

---

## Overview

Users receive a fresh daily deck with budget caps to prevent overwhelming choice and maintain quality discovery.

---

## Budget Rules

**Daily Total Cap:** 5 candidates maximum per deck

**Evidence:** [MomentsRules.cs:5](../../../backend/WovenBackend/Services/Moments/MomentsRules.cs#L5)
```csharp
public const int DailyTotalCap = 5;
```

**Daily Pending Cap:** Maximum 2 active balloons (DRAWN tab)

**Evidence:** [MomentsRules.cs:6](../../../backend/WovenBackend/Services/Moments/MomentsRules.cs#L6)
```csharp
public const int DailyPendingCap = 2;
```

---

## Deck Refresh

**Frequency:** Daily at midnight UTC

**Worker:** `DailyDeckOrchestrator` runs on schedule, generates new deck for active users

**Evidence:** [DailyDeckOrchestrator.cs](../../../backend/WovenBackend/Services/Matchmaking/DailyDeckOrchestrator.cs)

---

## Related

- [Deck Selection Service](./backend.md#deck-selection)
- [Moments API](./api.md)
- [Business Rules](../../business/rules.md#deck-size)
