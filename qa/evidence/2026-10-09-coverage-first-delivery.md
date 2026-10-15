# Coverage-first delivery - 2026-10-09

Founder sequencing is authoritative: code/feature suites and>=80% backend/frontend application line and branch coverage first, plus scoped coding-agent PR/QA review. Sandbox/persona/performance and consolidated bug/cause campaigns come later. No new sandbox, persona, provider or performance campaign ran in this phase. No app implementation changes, merge or deployment.

Added**219 executable tests**, all219passing in their final validated runs. Current totals: backend173tests172pass/1pendingSaved expectation; frontend278tests275pass/3previouslyrecorded component/service contracts. Failures stay visible; this phase did not open a new broad bug campaign. Early test-code issues (typed import/option shape, body-detection feature, asynchronous fixture readiness and selector) were corrected before reporting final results.

| Scope | Lines | Branches |
|---|---:|---:|
| Backend new unit/registered-handler run |16.18%|11.68%|
| Backend union with archived identical-binary HTTP counters |35.55%|19.16%|
| Frontend all72non-spec appTypeScript files |55.62%|59.98%|

Both80% gates fail. The archived backendHTTP counters are reused only after binary identity verification; no new HTTP sandbox requests were made. Generated migrations remain separate;21externalHTML templates remain an additional unmeasured obligation. Rendered-control tests cover selected inline templates and workflows, not every browser/device journey.

New test areas: profile/settings/privacy controls, tile creation/pinning, match previews, conversation list updates, chat send/trial/game/date-interest paths, game/decision controls, Commons pagination/orbits/dwell, foundational answer/photo contracts, AI-profile data preparation/sanitization/pair alignment/cohort fallback, game quotas/round progression/scoring/completion, endpoint claim parsing and coaching/feedback registered-handler contracts. AI-profile and game-agent tests use local data and controlled doubles; they do not claim real model quality. Registered-handler tests bypass auth middleware and PostgreSQL semantics explicitly.

Agent review: current21openPRheads inventoried. PR142 e02b628b documentation claims independently checked against source and remaining Markdown references. ScopedQA task139 closed under its acceptance requiring docs corrections, changed-file list, draftPR and handoff. PR142 remains open/draft/unmerged; shared CI163 and unrelated dimension drift164 remain separate. Other scoped reviews remain subject to their real acceptance/integration requirements; they were not closed just to reduce counts.

[Executed cases and hashes](2026-10-09-coverage-first-cases.json), [frontend per-file counts](2026-10-09-coverage-first-frontend.json), [backend source/IL counter union](2026-10-09-coverage-first-backend-combined.json), [frontend gate](2026-10-09-coverage-first-frontend-gate.json), [backend gate](2026-10-09-coverage-first-backend-gate.json). Registry479 named cases; no complete-product requirement coverage claim.

Reproduce with existing local tooling: `run-frontend-qa.mjs` against auth176cf74 candidate; `run-backend-qa.mjs` against startup5461c85 candidate. All test tools stay local; strategy and coverage-plan files now enforce the founder ordering. Next uncovered areas include Home/assistant/landing/client services and backend endpoint/service logic. The80% primary target remains active and unfinished.
