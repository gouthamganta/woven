# Duplicate-choice transaction regression — 2026-10-08

**Failed.** Candidate `5461c85`, local Production runtime, isolated model-created
`woven_qa_model` schema and synthetic actors. No real users or paid calls involved.

After one successful distinct warmup choice, four simultaneous identical TODAY
choices with the same request key returned **200, 409, 409, 500**. Independent
PostgreSQL inspection then found **3 daily-budget slots used, 2 saved responses,
2 notes**: the warmup plus one intended new action. The failed duplicate consumed
an extra slot without a saved action. The 500 also lacked the correlation response
header present on success/conflict responses.

This demonstrates an outer transaction/deduplication failure; the earlier
eight-distinct-target cap test passing did not prove duplicate safety. Inner
retry-compatible transactions are insufficient to guarantee the entire choice
operation remains consistent. Rejected/failed duplicates must not debit. Accepted
replays may return the same committed result or a defined conflict according to
the source contract; do not require a new idempotency policy merely from this test.

Claude handoff [#124](https://github.com/gouthamganta/woven/issues/124) moved to
**Changes requested / owner Claude**. Error-path trace correlation handed to
[#108](https://github.com/gouthamganta/woven/issues/108). Test code is
`qa/scripts/duplicate-choice-race.mjs`; [sanitized results](2026-10-08-duplicate-choice-race.json)
record response statuses and persisted verification, with private artifact hash.

The probe mutates designated synthetic actor state; reruns must use untouched
actors or a deliberate verified-local fixture reset. It is not a generic safe
production smoke test. No database was reset, schema migrated, branch merged or
deployment triggered. Provider authentication, AI quality and migration readiness
remain outside this result. One actor/date/race observation does not quantify the
frequency of failure under real-user traffic.
