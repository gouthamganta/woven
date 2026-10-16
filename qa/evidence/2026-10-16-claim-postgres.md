# Atomic claim retest — 2026-10-16

Application commit: `483c942aaafd8a31983d6da784d6d1ffd6be1028`, PR #195. Real PostgreSQL 16.14, minimal isolated three-table schema. **Eight selected checks: one passed, seven failed.** Six failures share one runtime cause; the seventh demonstrates rejection of interrupted-claim recovery. [JSON results](2026-10-16-claim-postgres.json).

## P1: SQL API overload prevents integration

AfterthoughtIntegrationService.cs lines 50–56 passes timestamp, ID and CancellationToken as separate arguments to ExecuteSqlRawAsync. This selects the params-object overload: the token becomes a SQL parameter. Actual configured-retry and retry-disabled executions both throw:

```
InvalidOperationException: The current provider doesn't have a store type mapping for properties of type 'CancellationToken'.
```

Sequential replay, overlapping scopes, prefix-ID integration, pending deferred endpoint and existing exact-signal completion also encounter this same cause. These do not independently prove defects in each downstream behavior. Reset SQL at lines 101–104 uses the same incorrect argument pattern. Pass an explicit parameter collection and cancellation token through the correct overload, or an appropriate interpolated SQL API.

## P1: claim marker is indistinguishable from completion

The atomic UPDATE commits ProcessedAt before integration starts. A process exit afterwards leaves it permanently set without the required signal or trust action. No lease/expiry/recovery owner is present. Loading the claimed row is outside try/catch; cleanup also reuses the request cancellation token. These are static observations, not crash/cancellation fault-injection results.

Executed recovery check: seed the exact persisted interrupted-claim state (ProcessedAt set, zero signals), then invoke the actual deferred endpoint handler. It returns **400 AFTERTHOUGHT_ALREADY_SUBMITTED**, confirming that the public recovery route cannot distinguish this state from success. This uses a synthetic persisted-state fixture, not an actual killed process. Preserve a completion marker that means completed; use a retry-aware transaction around effect plus completion, or a durable recoverable claim with separate state.

## Exact JSON control passes

Executed the new PostgreSQL JSON extraction/integer comparison: ID 10 does not equal 1; ID 1 equals 1. The SQL-level prefix defect is fixed. End-to-end signal deduplication cannot be accepted until the earlier runtime failure is fixed.

## Reproduction and limits

Harness: qa/probes/afterthought-postgres/. Set WOVEN_QA_APPLICATION_SHA to the tested full SHA and WOVEN_QA_CASES to these comma-separated names:

```
configured_retry_strategy_processes_feedback,retry_disabled_control_and_sequential_replay,separate_scopes_skip_locked_without_duplicate,dedup_distinguishes_complete_afterthought_id_from_prefix,pending_deferred_endpoint_reaches_real_integration,existing_exact_signal_repairs_completion_marker,interrupted_claim_deferred_retry_is_not_rejected,exact_json_id_sql_control
```

Run `dotnet run --project qa/probes/afterthought-postgres/AfterthoughtPostgres.csproj --no-restore --verbosity quiet` with the guarded local connection and report path configured as documented. Cases tied to the previous transaction design were excluded rather than misreported against this implementation. Harness reports the supplied source revision; an initial exploratory run was superseded by this correctly identified selected-case report.

Database/user identity checked before EnsureCreated. Fresh disposable QA container reused by name after its previous tmpfs contents vanished on stop; no existing application database reset. Container stopped after this run. The user-authorized removal of stale Woven app images remains intact: only PostgreSQL was started, and current application source was compiled locally. No full 219-case, migration, trust, worker, UI, deployment or paid-API acceptance claim. No application files changed. Next owner Claude; PR195 not merge-ready.
