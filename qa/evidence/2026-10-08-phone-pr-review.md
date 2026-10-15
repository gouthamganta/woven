# Independent phone PR review — 2026-10-08

Codex reviewed the three open `codex-phone/*` drafts and Claude-authored Product
Rules PR. All use the same GitHub account; branch prefixes identify the Codex
phone changes, while PR #140's content identifies the Claude product document.
No additional branch explicitly named `claude-phone/*` appeared in the open PR
list at this checkpoint. This does not establish the originating device of every
commit. Nothing was merged or deployed.

| PR / exact head | Independent result | Remaining constraint |
|---|---|---|
| #137 `dce8c896a0593ddea50f0ce834bd125ef0815cd2` | 12/12 backend tests; production frontend build; four desktop/mobile Deck/Drawn browser checks passed | Mocked browser APIs, InMemory handler tests; real PostgreSQL/proxy/provider coverage still required; CI red |
| #141 `5de72ae75a659750e1866a002d082186fe1faaa9` | 8/8 existing tests + 6/6 new cleanup regressions passed; callers traced | Conflicts with #144 in budget/rule files; retry/cap/database integration not signed off; CI red |
| #142 `e02b628b20b2e31a7832a56a4784b594e42ad019` | All 12 changed application/doc files reviewed for canonical pillar/q6 claims; those claims match code | Existing embedding-dimension drift queued separately; not a full audit of unchanged docs; CI red |
| #140 `e387d029875c6ac1ca7ce4d01bd2e8ae3c9160c8` | Source-of-truth document reviewed | Not marked draft; M-4 and section 5 still contradict on ChatNotes; correction required |

## Rating-removal evidence

Phone #137 tests call the actual registered handlers with a manually supplied
synthetic principal, InMemory database and fake deck/photo services. Four cases
assert non-empty cards and absence of the rating field with zero/five ratings.
They deliberately omit PostgreSQL vector mappings and do not test middleware
authentication or SQL translation. Codex independently ran them locally.

The actual production-built frontend was loaded in Chrome at 1366×768 and
390×844. Synthetic API interception supplied visible Deck/Drawn cards with
legacy `rating` fields. Both tabs rendered and no `.flagBar` elements appeared
in all four final checks. External requests were blocked; no live API/proxy/SSO
or provider behavior was established. The notification nudge was dismissed
through its normal Later button; no forced clicks or CSS suppression.

Initial incomplete Pulse/coaching fixtures created unrelated modal overlays;
fixtures were corrected to their contracts. The first mobile tab attempt also
encountered the notification nudge. These setup observations were not reported
as phone rating-removal defects. Raw final results/screenshots remain local.

Initial frontend build failed in LMDB cache with “Not enough space.” The disk
had substantial free space; its precise cache-resource cause was not established.
Recovery disabled persistent cache and used two workers in the isolated checkout.
Production build then passed with the existing 40.92KB/20KB stylesheet warning.
Temporary workspace configuration was restored afterward. No dependency or
phone-branch source changes were pushed.

## Cleanup and integration

Six additional `SavedCleanupContractTests` verify recent/older historical Saved
records no longer boost candidates or get deleted; Magical, Logical and legacy
Yes boosts remain; empty candidate lists return empty. All six pass against the
exact #141 backend. InMemory is not proof of PostgreSQL constraints/retries/caps.

A local merge probe of #137 onto `5461c85` auto-merged and its 12 tests passed
on the uncommitted combined source. That is a second run of the same tests,
not 24 distinct tests. The probe was aborted after testing.

A separate #141 merge probe conflicts in:

- `backend/WovenBackend/Services/Moments/InteractionBudgetService.cs`
- `backend/WovenBackend/Services/Moments/MomentsRules.cs`

It was aborted without choosing a side. Integration must preserve the founder's
72-hour value and the retry-compatible serializable transaction repairs. Choosing
the older phone file wholesale could undo these fixes. GitHub mergeability against
current master does not establish compatibility between the two pending PRs.

One early local command used a wrong relative checkout path and therefore reran
#137 while naming its output “pr141-baseline.” That raw file is excluded from all
#141 evidence. Subsequent checkout verified full `5de72ae...` and produced the
correct `pr141-verified-head.trx`; only that run is reported above.

## Documents and CI

`FoundationalQuestionBank.CanonicalPillars` lists Lifestyle, Energy, Values,
Communication, Ambition, Stability, Curiosity, Affection. q6 tags Ambition and
Curiosity. #142's corrected names/coverage match this source. A pre-existing
128-versus-1536 PillarEmbedding claim remains in `docs/systems/echo/embeddings.md`,
where the EF model maps `vector(1536)`; follow-up [#164](https://github.com/gouthamganta/woven/issues/164).
This is separate from the correct phone naming change.

Founder already decided: show both ChatNotes to the matched pair. #140 M-4
matches that, section 5 still says background-only. Correction remains
[#143](https://github.com/gouthamganta/woven/issues/143); no new founder decision
is needed. Desired First Ten/Last Call behavior is not implemented merely because
the document defines it.

All four PR heads have failed C# Format, Large file and Terraform Validate jobs.
Representative run logs `37799786484`/`37799786380` show:

- Terraform stops at `fmt -check` on `infra/modules/container_apps/main.tf`, exit 3,
  before init/validate; no Terraform cloud action was run by Codex.
- Large-file check scans the entire checkout and finds existing intro-video assets;
  those PR diffs do not add those assets.
- C# formatting has 531 error lines, including three introduced whitespace errors
  in #137's new test, lines 51–52. The whole failure is not attributed to old debt.

Scoped repair is queued in [#163](https://github.com/gouthamganta/woven/issues/163).
Targeted feature passes do not override failed merge gates. No automatic approval,
issue closure, master merge, deployment, paid API call or database reset occurred.
Independent findings were posted directly to PRs #137/#140/#141/#142 and linked
Issues #119/#138/#139/#143/#109; founder relay is unnecessary.
