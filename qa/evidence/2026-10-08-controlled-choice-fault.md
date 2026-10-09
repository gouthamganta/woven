# Controlled choice-write failure

Candidate5461c85, isolated model-created PostgreSQL and unused synthetic actor.
An actor/target-specific temporary BEFORE note-insert trigger forced a database
write error after budget spending. Initial budget/responses/notes were0/0/0.
The request returned500; afterward budget was1, responses0 and notes0.
**Failed:** quota spending persists without the failed action.

Trigger/function removed in finally; independent catalog queries confirmed zero
remaining QA fault triggers/functions. This script is not production-safe and
must run only against the fenced local synthetic database. No schema migration,
real-user data, paid provider or deployment involved.

A fresh natural duplicate race on the same old application passed on another
actor. This is evidence of timing-dependent reproduction, not that the earlier
persisted failure disappeared. Controlled rollback testing is required before
closing #124. The bounded local Claude implementation attempt produced no edits
and exited1 with empty logs; no repair was claimed or paid fallback attempted.

Harness: `qa/scripts/choice-fault-check.mjs`; independent state verification is
built into the race harness. Raw results are retained locally and sanitized
JSON accompanies this evidence.
