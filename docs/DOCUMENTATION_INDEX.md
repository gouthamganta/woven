# Woven Documentation Index

All documentation is evidence-based — every claim is traceable to source code, configuration, migrations, or Terraform resources. No generic boilerplate. No aspirational claims.

This index covers 43 files written in the 2026-05-26 documentation sprint. Legacy files (`docs/ARCHITECTURE.md`, `docs/API.md`, `docs/INFRASTRUCTURE.md`, etc.) are superseded by the files below.

---

## By audience

**I'm a new engineer** → [LOCAL_SETUP.md](contributing/LOCAL_SETUP.md) → [ONBOARDING.md](contributing/ONBOARDING.md) → [CONTRIBUTING.md](contributing/CONTRIBUTING.md)

**I'm implementing a feature** → [BACKEND_DESIGN.md](technical/BACKEND_DESIGN.md) → [API_DOCUMENTATION.md](technical/API_DOCUMENTATION.md) → [SERVICES_DOCUMENTATION.md](technical/SERVICES_DOCUMENTATION.md)

**I'm touching ECHO / matchmaking** → [AI_INTELLIGENCE_DEEP_DIVE.md](ai_intelligence/AI_INTELLIGENCE_DEEP_DIVE.md) → [SIGNALS_VECTORS_SCORING.md](signals/SIGNALS_VECTORS_SCORING.md) → [RESEARCH.md](research/RESEARCH.md)

**I'm working on the frontend** → [FRONTEND_DESIGN.md](technical/FRONTEND_DESIGN.md) → [COMPONENTS_PAGES_TEMPLATES.md](technical/COMPONENTS_PAGES_TEMPLATES.md)

**I'm preparing for a fundraise** → [PITCH_DECK_GUIDE.md](investor/PITCH_DECK_GUIDE.md) → [DIFFERENTIATION.md](investor/DIFFERENTIATION.md) → [FUNDRAISING.md](founder/FUNDRAISING.md)

**I'm a PM or designer** → [PRODUCT_STORY.md](product/PRODUCT_STORY.md) → [FEATURES.md](product/FEATURES.md) → [FLOWCHARTS.md](flowcharts/FLOWCHARTS.md)

**I'm deploying or operating** → [DEVOPS.md](technical/DEVOPS.md) → [CLOUD_INFRASTRUCTURE.md](technical/CLOUD_INFRASTRUCTURE.md) → [MONITORING_OBSERVABILITY.md](technical/MONITORING_OBSERVABILITY.md)

---

## Technical

| File | What it covers |
|---|---|
| [BACKEND_DESIGN.md](technical/BACKEND_DESIGN.md) | .NET 10 Minimal API architecture, endpoint patterns, DI container, all route groups |
| [ARCHITECTURE.md](technical/ARCHITECTURE.md) | System architecture, service boundaries, data flow, component relationships |
| [SYSTEM_DESIGN.md](technical/SYSTEM_DESIGN.md) | System design decisions, CAP tradeoffs, scaling approach |
| [AI_ML_DOCUMENTATION.md](technical/AI_ML_DOCUMENTATION.md) | ECHO pipeline end-to-end, OpenAI integration, all embedding services |
| [DATABASE_DESIGN.md](technical/DATABASE_DESIGN.md) | Full PostgreSQL schema, all 60+ entities, CHECK constraints, pgvector setup |
| [API_DOCUMENTATION.md](technical/API_DOCUMENTATION.md) | All REST endpoints, request/response shapes, auth requirements |
| [FRONTEND_DESIGN.md](technical/FRONTEND_DESIGN.md) | Angular 21 architecture, OnPush strategy, service layer, HTTP patterns |
| [COMPONENTS_PAGES_TEMPLATES.md](technical/COMPONENTS_PAGES_TEMPLATES.md) | All pages and components, routes, responsibilities |
| [CLOUD_INFRASTRUCTURE.md](technical/CLOUD_INFRASTRUCTURE.md) | Azure resources, VNet topology, 4-pod Container Apps, batch worker isolation |
| [DEVOPS.md](technical/DEVOPS.md) | CI/CD pipeline (ci.yml + deploy.yml), Dockerfiles, ACR build, smoke checks |
| [SECURITY.md](technical/SECURITY.md) | Auth, JWT, AES-256-GCM, prompt injection protection, audit logging, block system |
| [SERVICES_DOCUMENTATION.md](technical/SERVICES_DOCUMENTATION.md) | All backend services by domain, batch worker schedule table |
| [THIRD_PARTY_INTEGRATIONS.md](technical/THIRD_PARTY_INTEGRATIONS.md) | All 10 external integrations — OpenAI, Google OAuth, Azure, SpeechBrain, Replicate, Google Places |
| [ENCRYPTION_SECURITY_DESIGN.md](technical/ENCRYPTION_SECURITY_DESIGN.md) | AES-256-GCM field table, key rotation, PII sanitizer, prompt injection patterns |
| [TESTING.md](technical/TESTING.md) | Test commands, CI pipeline mechanics, smoke checks, known gaps |
| [MONITORING_OBSERVABILITY.md](technical/MONITORING_OBSERVABILITY.md) | App Insights, structured logging conventions, cost tracking, health endpoints |
| [TECHNICAL_DEBT_AND_IMPROVEMENTS.md](technical/TECHNICAL_DEBT_AND_IMPROVEMENTS.md) | All known gaps, intentional tradeoffs, improvement opportunities |

---

## AI Intelligence

| File | What it covers |
|---|---|
| [AI_INTELLIGENCE_DEEP_DIVE.md](ai_intelligence/AI_INTELLIGENCE_DEEP_DIVE.md) | ECHO's AI components in depth — prompts, feedback loops, tone adaptation, date style learning |

---

## Signals, Vectors, Scoring

| File | What it covers |
|---|---|
| [SIGNALS_VECTORS_SCORING.md](signals/SIGNALS_VECTORS_SCORING.md) | All signal event types, 9 embedding modalities with dimensions, 16-component scoring weights |

---

## Product

| File | What it covers |
|---|---|
| [PRODUCT_STORY.md](product/PRODUCT_STORY.md) | What Woven is, every surface explained, what the app explicitly does not do |
| [HIGH_LEVEL_DESIGN.md](product/HIGH_LEVEL_DESIGN.md) | Product-level system design, surface anatomy, trial state machine, match close paths |
| [USER_LIFECYCLE.md](product/USER_LIFECYCLE.md) | End-to-end user journey from install to planned date, with Mermaid lifecycle flowchart |
| [FIRST_TIME_USER_GUIDE.md](product/FIRST_TIME_USER_GUIDE.md) | New user guide for every major surface — deck, Drawn, match, trial, Find Love, Commons |
| [FEATURES.md](product/FEATURES.md) | Exhaustive 22-section feature inventory, what each feature produces as a signal |

---

## Contributing

| File | What it covers |
|---|---|
| [CONTRIBUTING.md](contributing/CONTRIBUTING.md) | Code philosophy, branch strategy, 0-errors mandate, signal recording requirement, design rules |
| [LOCAL_SETUP.md](contributing/LOCAL_SETUP.md) | Step-by-step local dev setup: Docker, secrets, migrations, pgvector caveat |
| [ONBOARDING.md](contributing/ONBOARDING.md) | New engineer orientation — what to read first, ECHO pipeline, trial period, invisible AI principle |
| [SEED_DATA_AND_PERSONAS.md](contributing/SEED_DATA_AND_PERSONAS.md) | No automated seed tooling — manual test persona guide, ECHO thresholds explained |

---

## Flowcharts

| File | What it covers |
|---|---|
| [FLOWCHARTS.md](flowcharts/FLOWCHARTS.md) | 5 Mermaid diagrams: user lifecycle, ECHO ML pipeline, balloon pop → match, trial period, Find Love |

---

## Investor

| File | What it covers |
|---|---|
| [MARKET_DYNAMICS.md](investor/MARKET_DYNAMICS.md) | Market size, swipe fatigue, behavioral ML opportunity, white space |
| [COMPETITIVE_LANDSCAPE.md](investor/COMPETITIVE_LANDSCAPE.md) | Tinder/Hinge/Bumble/CMB/Thursday analysis, 9-row differentiation matrix |
| [POSITIONING.md](investor/POSITIONING.md) | Premium-intentional quadrant, invisible AI thesis, how each product decision reinforces position |
| [DIFFERENTIATION.md](investor/DIFFERENTIATION.md) | 10 deep technical differentiators with full implementation detail |
| [TARGET_USERS.md](investor/TARGET_USERS.md) | Primary user profile, behavioral characteristics, the ECHO flywheel, anti-target description |
| [SCALABILITY.md](investor/SCALABILITY.md) | Production infrastructure, horizontal scaling, AI cost model, honest scaling limits |
| [PITCH_DECK_GUIDE.md](investor/PITCH_DECK_GUIDE.md) | 10-slide deck content guide — what to include per slide, what not to invent |

---

## Research

| File | What it covers |
|---|---|
| [RESEARCH.md](research/RESEARCH.md) | Design rationale for all 12 core ECHO decisions — why logistic regression, why 0.5 cold-start neutral, why trial period, why voice embedding |

---

## Founder

| File | What it covers |
|---|---|
| [FUNDRAISING.md](founder/FUNDRAISING.md) | Technical moat narrative, what to demonstrate live, what to disclose, metrics story |
| [FORECASTS.md](founder/FORECASTS.md) | Unit economics framework, flywheel threshold, linear vs. step-function costs, premium design space |
| [AI_IN_DEVELOPMENT.md](founder/AI_IN_DEVELOPMENT.md) | How Claude Code was used throughout development — architecture, implementation, code review, docs |
| [CLAUDE_CODE_SETUP.md](founder/CLAUDE_CODE_SETUP.md) | How to use Claude Code in this project — CLAUDE.md, memory system, common task guides |

---

## Meta

| File | What it covers |
|---|---|
| [DOCUMENTATION_THINKING.md](meta/DOCUMENTATION_THINKING.md) | Philosophy and process behind this suite — evidence-only mandate, parallel agent strategy, decay mitigations |

---

## Legacy files (superseded)

These files predate the 2026-05-26 sprint and are kept for reference but should not be trusted over the files above:

- `docs/ARCHITECTURE.md` → superseded by `technical/ARCHITECTURE.md`
- `docs/API.md` → superseded by `technical/API_DOCUMENTATION.md`
- `docs/INFRASTRUCTURE.md` → superseded by `technical/CLOUD_INFRASTRUCTURE.md`
- `docs/BUSINESS_RULES.md` → superseded by `technical/BACKEND_DESIGN.md` + `product/FEATURES.md`
- `docs/DATA_FLOW.md` → superseded by `signals/SIGNALS_VECTORS_SCORING.md` + `technical/SYSTEM_DESIGN.md`
- `docs/GLOSSARY.md` → superseded by the feature vocabulary table in `contributing/ONBOARDING.md`
- `docs/STATE_MACHINES.md` → superseded by `flowcharts/FLOWCHARTS.md`
- `docs/WOVEN_COMPLETE.md` → superseded by this entire suite
- `docs/CONTRIBUTING.md` → superseded by `contributing/CONTRIBUTING.md`
- `docs/ai/` → superseded by `ai_intelligence/AI_INTELLIGENCE_DEEP_DIVE.md`
