# Issue #138 — Saved/Pending caller trace

Starting commit: master 3df9759. Branch: codex-phone/138-remove-saved-pending.

## Static trace before edits

- `Services/Moments/MomentsRules.cs:6`: DailyPendingCap definition.
- `Services/Moments/InteractionBudgetService.cs:21,45-49,82-91,101-104`:
  Pending enum, Redis/DB caps, increment and response bookkeeping. No callers
  of SpendType.Pending outside this service.
- `Services/CacheKeys.cs:9`: PendingCounter, only called by that service.
- `Endpoints/MomentsEndpoints.cs:531-538,727-733`: Drawn (LIKED_YOU)
  spends via SparkWalletService; Deck uses SpendType.Moment. The choice switch
  at 476-488 rejects PENDING; /choose likewise accepts only positive choices.
- `Services/Matchmaking/DeliveryBoostService.cs:17,109-124`: old Saved-only
  PendingMatches boost; removed. No PendingMatch writes in active endpoints.
- `Endpoints/SupportEndpoints.cs:20,27`: stale Save action/Saved navigation
  guidance; corrected to existing choices/Drawn.
- Frontend scan of services/pages/components: no profile Saved/Pending action,
  endpoint or UI. Pulse saved events indicate submitted daily answers and are
  unrelated. Game PENDING status is unrelated.

Searches: rg DailyPendingCap, SpendType.Pending, PendingCounter, PendingUsed,
PendingMatches, MomentChoice.PENDING, Saved/save/pending across backend/frontend.
After edits no Saved budget/cache or PendingMatches service/endpoint usages remain.

## Data preservation and limitations

No migration or schema/model changes. DailyInteraction.PendingUsed, PendingMatch
mapping/table and historical MomentChoice.PENDING values retained for old data.
Historical response sample/documents are not active UI or endpoints. No real data
or services accessed. Browser/PostgreSQL integration remains for laptop QA.

## Checks

- dotnet build (backend/WovenBackend): exit 0, 0 warnings/errors, SDK 10.0.401.
- npx ng build (frontend/woven-frontend): exit 0; existing landing-simple
  stylesheet budget warning (40.92 kB / 20 kB). Reused installed node_modules;
  no dependency manifest/lock changes.
- dotnet test (backend/WovenBackend.Tests): exit 0, 8/8 passed;
  existing MSB3277 EF Relational 10.0.4/10.0.12 warning remains.
- git diff --check: passed.
