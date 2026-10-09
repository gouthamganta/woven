# Woven Documentation Index

**Welcome to Woven's documentation.** Everything is organized by feature and system, not by audience. Find what you need quickly.

---

## 🚀 Quick Start

**New to Woven?**
1. Read [QUICKSTART.md](QUICKSTART.md) (5 minutes)
2. Set up locally: [development/setup.md](development/setup.md)
3. Understand the codebase: [architecture/README.md](architecture/README.md)

**Looking for something specific?**
- Use the sections below
- Each folder has a README.md for overview
- Detailed docs inside each folder

---

## 📱 FEATURES (What Users See)

User-facing features and experiences.

| Feature | What | Docs |
|---------|------|------|
| **Onboarding** | User registration and profile setup | [features/onboarding/](features/onboarding/) |
| **Moments** | Daily deck (Deck + Drawn tabs) | [features/moments/](features/moments/) |
| **Sparks** | Daily spark economy | [features/sparks/](features/sparks/) |
| **Chats** | Messaging, trial, balloon, Find Love | [features/chats/](features/chats/) |
| **Games** | Interactive chat games (KnowMe, RedGreenFlag) | [features/games/](features/games/) |
| **Matches** | Match types, explanations, profiles | [features/matches/](features/matches/) |
| **Commons** | User content feed (Tiles + Orbit) | [features/commons/](features/commons/) |
| **Profile** | User profile, settings, my tiles | [features/profile/](features/profile/) |
| **Assistant** | Unified Woven Assistant | [features/assistant/](features/assistant/) |
| **Notifications** | Push notifications, web push | [features/notifications/](features/notifications/) |

---

## ⚙️ SYSTEMS (How It Works)

Backend infrastructure and cross-feature systems.

| System | What | Docs |
|--------|------|------|
| **ECHO** | AI matchmaking and learning | [systems/echo/](systems/echo/) |
| **Authentication** | Login, JWT, sessions | [systems/auth/](systems/auth/) |
| **Embeddings** | Multi-modal embedding generation | [systems/embeddings/](systems/embeddings/) |
| **Media** | File upload, storage, delivery | [systems/media/](systems/media/) |
| **Encryption** | Data encryption, key rotation | [systems/encryption/](systems/encryption/) |
| **Moderation** | Content moderation, safety | [systems/moderation/](systems/moderation/) |
| **Trust & Verification** | User verification, trust scoring | [systems/trust/](systems/trust/) |
| **Feedback** | Date feedback collection | [systems/feedback/](systems/feedback/) |
| **Coaching** | Weekly coaching summaries | [systems/coaching/](systems/coaching/) |
| **Analytics** | Behavior tracking, retention | [systems/analytics/](systems/analytics/) |
| **Caching** | Redis caching layer | [systems/caching/](systems/caching/) |
| **Queue** | Background job processing | [systems/queue/](systems/queue/) |
| **Seasons** | Seasonal features | [systems/seasons/](systems/seasons/) |
| **Venues** | Date venue recommendations | [systems/venues/](systems/venues/) |

---

## 🏗️ ARCHITECTURE

Technical architecture and system design.

- **[README.md](architecture/README.md)** - System architecture overview
- **[backend.md](architecture/backend.md)** - .NET architecture
- **[frontend.md](architecture/frontend.md)** - Angular architecture
- **[database.md](architecture/database.md)** - PostgreSQL schema
- **[infrastructure.md](architecture/infrastructure.md)** - Azure resources
- **[patterns.md](architecture/patterns.md)** - Code patterns
- **[deployment.md](architecture/deployment.md)** - Deployment architecture
- **[diagrams.md](architecture/diagrams.md)** - Architecture diagrams

---

## 📡 API REFERENCE

REST API documentation.

- **[README.md](api/README.md)** - API overview
- **[authentication.md](api/authentication.md)** - Auth headers, tokens
- **[rate-limiting.md](api/rate-limiting.md)** - Rate limits
- **[error-handling.md](api/error-handling.md)** - Error responses

**Endpoints by Feature:**
- [Onboarding](api/onboarding.md)
- [Moments](api/moments.md)
- [Chats](api/chats.md)
- [Matches](api/matches.md)
- [Commons](api/commons.md)
- [Profile](api/profile.md)
- [Notifications](api/notifications.md)
- [Games](api/games.md)
- [Media](api/media.md)

---

## 💻 DEVELOPMENT

Developer guides and workflows.

- **[README.md](development/README.md)** - Developer guide
- **[setup.md](development/setup.md)** - Local development setup
- **[contributing.md](development/contributing.md)** - How to contribute
- **[patterns.md](development/patterns.md)** - Code patterns
- **[testing.md](development/testing.md)** - Testing guide
- **[debugging.md](development/debugging.md)** - Common issues
- **[git-workflow.md](development/git-workflow.md)** - Branch strategy
- **[database-migrations.md](development/database-migrations.md)** - EF migrations
- **[deployment.md](development/deployment.md)** - Deploy guide
- **[monitoring.md](development/monitoring.md)** - Logs, App Insights

---

## 📊 BUSINESS

Business context and rules.

- **[README.md](business/README.md)** - Business overview
- **[glossary.md](business/glossary.md)** - Feature vocabulary
- **[rules.md](business/rules.md)** - Business rules
- **[state-machines.md](business/state-machines.md)** - State diagrams
- **[product-philosophy.md](business/product-philosophy.md)** - Design principles

---

## 🔒 SECURITY

Security documentation.

- **[README.md](security/README.md)** - Security overview
- **[authentication.md](security/authentication.md)** - Auth security
- **[encryption.md](security/encryption.md)** - Data encryption
- **[pii.md](security/pii.md)** - PII handling
- **[prompt-injection.md](security/prompt-injection.md)** - Prompt protection
- **[security-audit.md](security/security-audit.md)** - Audit log
- **[incident-response.md](security/incident-response.md)** - Incident process

---

## 📖 OTHER RESOURCES

- **[CLAUDE.md](../CLAUDE.md)** - Claude Code context (project overview)
- **[README.md](../README.md)** - Project README

### For Investors
- [investor/](investor/) - Investor materials

### Archive
- [archive/](archive/) - Old/superseded documentation

---

## 🔍 Finding What You Need

### I want to understand...

**...a user-facing feature:**
→ Check `features/[feature-name]/README.md`

**...how a system works:**
→ Check `systems/[system-name]/README.md`

**...the technical architecture:**
→ Check `architecture/README.md`

**...API endpoints:**
→ Check `api/README.md` then `api/[feature].md`

**...how to contribute:**
→ Check `development/contributing.md`

---

## 📝 Documentation Structure

Every feature/system folder follows this pattern:

```
feature-name/
├── README.md          ← Start here (for everyone)
├── user-experience.md ← How users interact
├── frontend.md        ← Angular implementation
├── backend.md         ← .NET implementation
├── api.md             ← API endpoints
└── ...                ← Feature-specific docs
```

---

## 🔄 Keeping Documentation Updated

Documentation is kept in sync with code through:
1. **Documentation-sync skill** - Alerts when docs need updates
2. **Evidence-based philosophy** - All claims traceable to code
3. **Regular audits** - Quarterly documentation reviews

**Last Major Update:** 2026-08-17  
**Last Audit:** 2026-08-17

---

## 🆘 Help

- **Questions?** Ask in the Woven Assistant or check [development/debugging.md](development/debugging.md)
- **Found an error?** Submit a PR or create an issue
- **Documentation outdated?** Alert the team or update it yourself

---

**Total Documentation:**
- 10 Features
- 14 Systems
- 5 Cross-cutting areas
- ~210 files

**Organization Principle:** By WHAT, not WHO
