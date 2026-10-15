# Client repair and data-integrity QA — 2026-10-08

## Client fix/retest

Claude produced three application-file changes in a bounded local subscription
session. Codex independently reviewed and ran the **same 37 frontend cases**:
baseline `3df9759` **18 passed / 19 failed**; candidate `176cf74` **37 passed / 0
failed**. Production build passed with the existing stylesheet budget warning.
Temporary cache settings were restored; no master merge or deployment.

Expanded candidate run: **51/51 passed**, adding all currently registered
protected component paths and root/app/catch-all redirect cases. Those additional
14 paths were not rerun against the baseline; do not infer a baseline51 count.

[Draft PR #167](https://github.com/gouthamganta/woven/pull/167) is stacked against
the startup/72h candidate #144. It restores protected-route/child checks, handles
invalid/expired payloads and storage failures, and restricts automatically added
bearer credentials to same-origin/configured API origins. Public routes and valid
development API requests are covered by positive cases.

This is client navigation/credential handling, **not JWT signature verification
or server account-state enforcement**. Manual-auth services, refresh/revocation,
cookie transport, real provider sign-in and wider browser/SSR cases remain open.
Server defects in #108 are not closed by these passes. Source/code was authored
by Claude; QA test changes and evidence are Codex-owned. No paid API fallback.
[Named outcomes and hashes](2026-10-08-frontend-auth-retest.json).

## Matched deletion failure

Candidate `5461c85`, actual local PostgreSQL model-created schema. DELETE of a
designated synthetic matched/blocked account returned500. Its own message count
changed1→0; SQL confirmed **user still present** and **matching vectors absent**.
This is partial deletion on an error path, not complete account erasure. The
error body carries correlationId/timestamp even though the response header is
absent; do not claim all tracing is lost.

Claude fix queued in [#165](https://github.com/gouthamganta/woven/issues/165).
Required coverage: unmatched/matched/blocked accounts, foreign-key-safe retention
and anonymization, DB rollback, blob ordering, retry recovery and counterpart
privacy. Current data policy must be respected rather than silently switched to
soft deletion. [Results](2026-10-08-matched-deletion.json). The test mutates only
the designated synthetic actor; it is not safe to run against real accounts.

## Encrypted identity lookup

Read-only console probe using the production EF encryption converter loaded a
synthetic user by ID, verified its decrypted email is a reserved fixture address,
then searched by that exact same email. Expected ID100; actual result null.
No row, key, token or plaintext email was changed/printed.

`AuthEndpoints` uses this equality query to reuse accounts when a provider-subject
identity is not already linked. Randomized encryption prevents plaintext equality
from matching ciphertext. This does not establish that all Google logins fail:
already-linked identities take a different path; real Google validation was not
exercised. Claude fix [#166](https://github.com/gouthamganta/woven/issues/166) must
preserve encryption and safe verified identity-linking policy; do not make all
encryption deterministic or scan/decrypt all production users as a workaround.
[Probe output](2026-10-08-email-lookup.json), code `qa/probes/`.

## QA importer correction

Codex's importer used the same unsuitable encrypted equality/suffix predicates.
Corrected it to read/decrypt a bounded **local synthetic-only** dataset, reject
non-fixture/duplicate identities, and reuse IDs from an in-memory fixture map.
Fixture validation now also rejects real-email domains and duplicate emails.
This local QA technique is not offered as production account lookup.

Executed repeat validation: first import restored **one** previously deleted
synthetic account and returned100 accounts; immediate second import inserted
**zero**, preserving identities. No database reset. Sandbox now has100 users;
the matched-deletion actor's partial state is deliberately retained for evidence,
not silently repaired. Concurrent seeding and migration readiness are unverified.
[Counts/hashes](2026-10-08-seed-repeat.json).

Four negative manifests (foreign email domain, duplicate email, underage persona,
non-synthetic marker) were rejected; independent SQL counts remained100 before
and after each attempt. Initial count harness omitted docker stdin and incorrectly
coerced empty output to zero. It was corrected to pass stdin and require a numeric
result, then all four checks reran. Only final100/100 results are reported.
[Guard results](2026-10-08-seed-guards.json).

All PostgreSQL checks use the isolated **model-created** schema; fresh/upgrade
migrations still lack sign-off. The duplicate-choice failure (#124), migration,
proxy, dependency and CI blockers remain. Full QA is not complete.
