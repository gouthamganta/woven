# Git Workflow

Branch strategy, commit messages, and PR process for Woven.

---

## Branch Strategy

### Main Branch

- **Name:** `master`
- **Protection:** PRs required, CI must pass, at least 1 approval
- **Direct commits:** Not allowed

---

### Feature Branches

**Naming convention:**
- `feature/short-description` — New features
- `fix/bug-description` — Bug fixes
- `chore/task-name` — Refactoring, docs, tooling

**Examples:**
```bash
feature/voice-notes
fix/chat-pagination
chore/update-dependencies
```

---

### Branch Lifecycle

1. **Create from master:**
```bash
git checkout master
git pull origin master
git checkout -b feature/your-feature
```

2. **Work on feature:**
```bash
# Make changes
git add .
git commit -m "feat: add voice note recording"
```

3. **Rebase on master before PR:**
```bash
git fetch origin
git rebase origin/master
```

4. **Push and open PR:**
```bash
git push origin feature/your-feature
# Open PR on GitHub
```

5. **After merge, delete branch:**
```bash
git checkout master
git pull origin master
git branch -d feature/your-feature
```

---

## Commit Messages

### Format

```
<type>: <description>

[optional body]

[optional footer]
```

### Types

| Type | Usage |
|---|---|
| `feat` | New feature |
| `fix` | Bug fix |
| `refactor` | Code refactoring (no behavior change) |
| `chore` | Tooling, dependencies, config |
| `docs` | Documentation only |
| `test` | Add or fix tests |
| `perf` | Performance improvement |

### Examples

**Good:**
```
feat: add voice note playback tracking

Records VoiceNoteListenComplete signal when user plays
a voice note to completion. Updates connection score
formula to include voice engagement (weight 0.05).

Source: ChatEndpoints.cs:402-450
```

**Also good (simple change):**
```
fix: correct trial period expiration calculation
```

**Bad:**
```
update stuff
fix bug
wip
```

---

## Pull Request Workflow

### 1. Open Draft PR Early

Open a **draft PR** as soon as you have working code worth reviewing. Do not wait for perfection.

**Benefits:**
- Get early feedback
- Show progress
- Catch issues sooner

---

### 2. Fill PR Description

**Template:**
```markdown
## What Changed
Brief description of what this PR does.

## Why
Context or problem this solves.

## Testing
What you tested and how.

## Checklist
- [ ] `dotnet build` produces 0 errors (backend)
- [ ] `npx ng build` produces 0 errors (frontend)
- [ ] Tests pass
- [ ] Design rules followed
```

---

### 3. Self-Review

Before requesting review:
1. Review your own diff on GitHub
2. Check for debug code, console.logs, commented code
3. Verify all new endpoints have `.RequireAuthorization()`
4. Ensure behavioral signals are recorded
5. Confirm no design rule violations

---

### 4. Request Review

- At least **1 reviewer required**
- No self-merges
- Address all requested changes
- Re-request review after changes

---

### 5. CI Must Pass

Both backend and frontend CI jobs must pass before merge.

**CI checks:**
- Build succeeds (0 errors)
- All tests pass
- No compilation warnings introduced

**If CI fails:**
1. Click "Details" to view logs
2. Fix the issue locally
3. Commit and push
4. CI re-runs automatically

---

### 6. Merge and Delete

After approval and green CI:
1. Click "Squash and merge" (preferred) or "Merge"
2. Delete branch after merge
3. Pull latest master locally

---

## Rebasing

### When to Rebase

**Before opening PR:**
```bash
git fetch origin
git rebase origin/master
```

**If master updated during PR:**
```bash
git fetch origin
git rebase origin/master
git push --force-with-lease origin your-branch
```

---

### Resolving Conflicts

```bash
git rebase origin/master

# If conflicts occur:
# 1. Open conflicted files, resolve markers
# 2. Stage resolved files
git add <file>

# 3. Continue rebase
git rebase --continue

# If you want to abort
git rebase --abort
```

---

## .gitignore

Key ignored files:

**Backend:**
- `bin/`, `obj/`
- `.vs/`, `.vscode/`
- `*.user`
- `appsettings.Development.json` (if contains secrets)

**Frontend:**
- `node_modules/`
- `dist/`
- `.angular/`

**Secrets:**
- `.env` (local env vars)
- `secrets.json` (user secrets are outside repo anyway)

**Never commit:**
- API keys
- Database passwords
- JWT signing keys
- Personal credentials

---

## Protecting master

**Branch protection rules (GitHub):**
- Require pull request before merging
- Require approvals (minimum 1)
- Dismiss stale approvals when new commits pushed
- Require status checks to pass (CI)
- Require branches to be up to date before merging
- Prohibit force pushes
- Restrict deletions

---

## Attribution

All commits and PRs include attribution:

**Commits:**
```
feat: add feature

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>
```

**PRs:**
```markdown
🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

---

**Last Updated:** 2026-10-07
