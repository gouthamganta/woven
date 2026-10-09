# Claude handoff retest

Tested HEAD: `aec9eac4ef5084ac972b1eb146b78de072331e65`.
Initial working tree: only untracked `AGENTS.md`. Full product audit remains
paused pending documentation completion. This run verifies the supplied handoff.

## Backend compilation — failed

Command:
`dotnet test backend/WovenBackend.Tests/WovenBackend.Tests.csproj --no-restore --logger "trx;LogFileName=backend-retest.trx" --results-directory qa/.local/test-results`

Exit 1, CS1061 at `Endpoints/InteractionEndpoints.cs(73,88)`:
`DateTimeOffset` does not contain a definition for `HasValue`.
No unit tests executed. CL-003 cannot be marked verified.

Current `CoachingSummary.cs` still declares `DateTimeOffset DeliveredAt`.
The predicate still calls `c.DeliveredAt.HasValue`. Neither supplied commit
contains the claimed application fix. Migration application was not verified.
Claude should supply the actual change/commit and schema migration if needed;
choose the intended delivery semantics before changing nullability.

## Development endpoint guards — source discrepancy

`Program.cs` around lines 920–925 registers development endpoints behind
`app.Environment.IsDevelopment()`. `DevAuthEndpoints.cs` also checks that
environment. No `#if DEBUG` guard was found in these files.

This confirms an environment guard, not the claimed compile-time exclusion.
It is not evidence of production exposure. A Release build running with
Development environment could still register these endpoints.
Runtime production absence remains untested. Use the actual POST methods for
route tests; a GET probe alone cannot establish absence of a POST endpoint.

## MessagePack provenance — confirmed

Command: `dotnet nuget why backend/WovenBackend/WovenBackend.csproj MessagePack`

Resolved path for net10.0:
`Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.1 → MessagePack 2.5.187`.
NU1902/NU1903 warnings persist. This establishes dependency provenance only;
advisory applicability, reachability, and compatible remediation remain CL-004.

## Product rule

Claude clarified that community ratings are never user-visible and CLAUDE.md
is authoritative. Accepted as the intended QA rule; UI behavior not yet verified.
