# Unified Woven Assistant

**Built:** 2026-08-17  
**Status:** SHIPPED

---

## What It Is

The Unified Woven Assistant is a single draggable floating orb that consolidates all user-facing interactions into one intelligent entry point. It replaces multiple scattered UI elements with a priority-driven task system that guides users through pending actions before opening the general chat interface.

### Core Concept

**One orb. Multiple modes. Priority-driven.**

The assistant dynamically shifts between three states:
1. **Pending Task Modal** — Date Feedback, Weekly Coaching, or Daily Pulse
2. **Assistant Chat** — Free-form support chat (when no pending tasks)
3. **Minimized Orb** — Persistent, draggable, badge-counted

Users never see multiple floating buttons or conflicting CTAs. The orb glows with task-specific colors, shows a badge count, and auto-advances through pending work.

---

## Design Goals

### 1. Reduce UI Clutter
Before: Separate floating buttons for Pulse, Coaching, Feedback, and Chat.  
After: One orb. All interactions flow through it.

### 2. Prioritize User Actions
Not all tasks are equal. Date Feedback (post-match feedback) takes absolute priority over weekly coaching summaries, which take priority over daily pulse check-ins.

### 3. Invisible Intelligence
The priority system is ECHO-driven but never surfaces scores or reasons to users. They see: "You have 3 pending" with a red glow. The orb handles the rest.

### 4. Draggable, Persistent, Non-Intrusive
The orb can be dragged anywhere on screen. Position persists across page navigations (stored in component state, not localStorage — deliberate choice to reset on full refresh).

### 5. Auto-Advance After Completion
When a user completes or skips a task, the assistant automatically shows the next pending task. If none remain, it opens the chat interface. No manual navigation required.

---

## User Flow

```
┌─────────────────────────────────────────────────────────────┐
│  User taps orb with badge "2"                                │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  Assistant checks pending tasks (priority sorted)            │
│  1. Date Feedback (priority 1) ← HIGHEST                     │
│  2. Daily Pulse   (priority 3)                               │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  Shows Date Feedback modal (full-screen overlay)             │
│  User either:                                                 │
│    A) Completes feedback → Logged: DateFeedbackCompleted     │
│    B) Skips            → Logged: DateFeedbackSkipped         │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  Auto-advance: Shows Daily Pulse modal                       │
│  User either:                                                 │
│    A) Completes pulse → Logged: DailyPulseCompleted          │
│    B) Skips           → Logged: DailyPulseSkipped            │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│  No more pending tasks → Opens assistant chat sheet          │
│  User can now ask free-form questions                        │
│  First open logged as: AssistantChatOpened                   │
└─────────────────────────────────────────────────────────────┘
```

---

## Key Features

### Priority System
See: [pending-tasks.md](./pending-tasks.md)

Tasks are ranked by ECHO importance:
- **Priority 1:** Date Feedback (post-match)
- **Priority 2:** Weekly Coaching
- **Priority 3:** Daily Pulse

The backend endpoint `/me/pending-tasks` returns tasks pre-sorted. Frontend simply renders the first task in the array.

### Badge & Glow States
See: [badge-glow.md](./badge-glow.md)

The orb displays:
- **Badge count** — Number of pending tasks (1, 2, 3…)
- **Glow color** — Visual priority signal based on highest-priority task:
  - 🔴 Red glow = Date Feedback pending
  - 🟡 Gold glow = Weekly Coaching pending
  - ⚪ White glow = Daily Pulse pending

### Skip Buttons
See: [skip-buttons.md](./skip-buttons.md)

Users can skip:
- **Daily Pulse** — "Skip" button logs `DailyPulseSkipped`, task stays incomplete
- **Weekly Coaching** — "Skip" button dismisses + disables future coaching summaries (user pref updated)

Date Feedback has no skip option (intentional — we need post-match data for ECHO).

### Auto-Advance Logic
See: [auto-advance.md](./auto-advance.md)

When a modal is completed or skipped:
1. Task removed from local `pendingTasks` array
2. Badge count decremented
3. Glow state recalculated
4. If `pendingTasks.length > 0` → show next modal
5. Else → open chat sheet, focus input field

No refetch required — all state managed in-memory during session.

### Interaction Logging
See: [signal-logging.md](./signal-logging.md)

Every user action is logged to `user_interaction_logs` table for ECHO training:
- `AssistantChatOpened`
- `DailyPulseCompleted` / `DailyPulseSkipped`
- `WeeklyCoachingDismissed` / `WeeklyCoachingSkipped`
- `DateFeedbackCompleted` / `DateFeedbackSkipped` (future)

Context JSON captures answers, next action, timing, etc.

---

## Why "Unified"?

Before this feature:
- Daily Pulse had its own floating button (lower left)
- Coaching cards appeared as full-screen takeovers
- Date Feedback was a separate route (`/feedback/:matchId`)
- Support chat was buried in settings

**Fragmentation problem:** Users didn't know where to go. Multiple entry points competed for attention. No clear priority.

**Unified solution:** One orb. One source of truth. Priority-driven flow. All interactions logged for ECHO.

---

## Architecture

### Frontend
- **Component:** `woven-assistant.component.ts`
- **Service:** `pending-task.service.ts`
- **Child modals:** `pulse-sheet.component.ts`, `coaching-card.component.ts`

See: [frontend.md](./frontend.md)

### Backend
- **Endpoints:** `InteractionEndpoints.cs` (`/me/pending-tasks`, `/me/interaction-log`)
- **Service:** `IInteractionLogService.cs`
- **Entity:** `UserInteractionLog.cs`

See: [backend.md](./backend.md)

---

## What's NOT Included (Yet)

- **Date Feedback integration** — Backend checks for pending feedback, but modal not yet wired to assistant flow. Currently a separate route.
- **Push notification triggers** — Orb doesn't pulse on new coaching/pulse arrival (no WebSocket listener).
- **Persistent orb position** — Position resets on page refresh. Deliberately simple for v1.
- **Chat history persistence** — Support chat history clears on close (ephemeral by design).

---

## ECHO Signal Value

Every interaction with the assistant generates trainable signals:

| Signal | ECHO Use Case |
|--------|---------------|
| `AssistantChatOpened` | Engagement depth, support dependency |
| `DailyPulseCompleted` | Mood/state tracking compliance |
| `DailyPulseSkipped` | User friction, survey fatigue |
| `WeeklyCoachingDismissed` | Coaching acceptance rate |
| `WeeklyCoachingSkipped` | Coaching rejection (with disable pref) |

All logged with `context` JSON for rich analysis. See [signal-logging.md](./signal-logging.md) for full event catalog.

---

## Design Inspiration

The draggable orb pattern is inspired by:
- **Intercom / Drift** — Persistent chat bubbles
- **Apple Assistive Touch** — Draggable, multi-function orbs
- **Notion's "?" button** — Single entry point for help

Key differentiator: **Priority-driven task flow** before chat. Most chat widgets open directly to chat. Woven Assistant prioritizes pending user actions first.

---

## Related Docs

- [pending-tasks.md](./pending-tasks.md) — Priority system, task detection
- [badge-glow.md](./badge-glow.md) — Badge count, glow states
- [skip-buttons.md](./skip-buttons.md) — Skip functionality
- [auto-advance.md](./auto-advance.md) — Auto-advance logic
- [signal-logging.md](./signal-logging.md) — Interaction events
- [frontend.md](./frontend.md) — Frontend architecture
- [backend.md](./backend.md) — Backend architecture

---

## Future Enhancements

### Planned
- Wire Date Feedback modal to assistant flow
- Add "Remind me later" option for Coaching (4h snooze)
- WebSocket listener to pulse orb on new coaching delivery

### Considered (Not Planned)
- Multi-orb mode (one per task type) — **Rejected:** defeats "unified" purpose
- Orb position sync to localStorage — **Rejected:** prefer stateless resets
- Voice input for chat — **Deferred:** accessibility > novelty for v1
