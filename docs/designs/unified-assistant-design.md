# Unified Woven Assistant Design
**Date:** 2026-08-16  
**Status:** Design Phase

---

## Overview

Consolidate all user check-ins and feedback into a single Woven Assistant entry point. The draggable orb becomes the unified interaction surface for:
- Daily Pulse (every 48h)
- Date Feedback (post-match)
- Weekly Coaching (ECHO insights)
- Chat Assistant (Q&A)

**Core Principle:** Single point of interaction, smart priority, rich signal capture.

---

## UX Flow

### Orb States

| State | Visual | Behavior |
|-------|--------|----------|
| **No pending** | Gold orb, no badge | Click → Assistant chat |
| **Pulse pending** | Soft white glow, badge (1) | Click → Daily Pulse modal |
| **Feedback pending** | Red glow, badge (1) | Click → Date Feedback modal |
| **Coaching ready** | Gold glow, badge (1) | Click → Weekly Coaching card |
| **Multiple pending** | Highest priority glow, badge (2+) | Click → Highest priority first |

### Priority Order
```
1. Date Feedback     (most urgent - fresh memory)
2. Weekly Coaching   (rare, high value)
3. Daily Pulse       (regular, can wait)
4. Assistant Chat    (always available)
```

### Click Flow
```
User clicks orb
  ↓
IF pending tasks exist:
  Show highest priority modal
  User completes OR skips
  ↓
  IF more pending:
    Auto-show next priority
  ELSE:
    Open assistant chat
ELSE:
  Open assistant chat
```

### Modal Structure
Every modal has:
- Content area (questions/feedback/insights)
- Primary action: "Save" / "Submit" / "Got it"
- Secondary action: "Skip" button (bottom-left, subtle)

---

## Signal Capture

### Events Tracked

| Event | When | Data Captured |
|-------|------|---------------|
| `DailyPulseCompleted` | User saves pulse | cycleId, answers, timeToComplete |
| `DailyPulseSkipped` | User skips pulse | cycleId, nextAction |
| `DateFeedbackCompleted` | User submits feedback | matchId, metInPerson, stars, texts, timeToComplete |
| `DateFeedbackSkipped` | User skips feedback | matchId, nextAction |
| `WeeklyCoachingDismissed` | User dismisses coaching | summaryId, timeSpentReading |
| `WeeklyCoachingSkipped` | User skips coaching | summaryId, nextAction |
| `AssistantChatOpened` | Opens chat after task | previousTask (pulse/feedback/coaching/none) |
| `AssistantMessageSent` | Sends message in chat | messageLength, topicCategory |

### Signal Schema
```typescript
{
  userId: number,
  eventType: string,
  occurredAt: Date,
  context: {
    // Task-specific IDs
    cycleId?: string,
    matchId?: string,
    summaryId?: number,
    
    // Timing metrics
    timeToComplete?: number,      // seconds from open to action
    timeSpentReading?: number,    // seconds spent on screen
    
    // User action
    nextAction?: 'opened_assistant' | 'closed_app' | 'opened_next_pending',
    
    // Device context
    deviceType?: 'mobile' | 'desktop',
    timeOfDay?: string,           // "morning" | "afternoon" | "evening" | "night"
    dayOfWeek?: string,           // "monday" | "tuesday" | ...
  }
}
```

### Learning Signals

**Completion Signals (positive):**
- Fast completion (<30s) = Easy, valuable
- Slow completion (>2min) = Thoughtful, engaged
- Consistent completion = Feature is valuable

**Skip Signals (negative or timing):**
- Immediate skip (<3s) = Not ready, wrong timing
- Skip → Chat = Had a question instead
- Skip → Close = Not interested right now
- Skip streaks (5+) = Feature not valuable, pause showing

---

## Component Architecture

### 1. Woven Assistant Component (Updated)
**Path:** `frontend/woven-frontend/src/app/components/woven-assistant/`

**New Responsibilities:**
- Check for pending tasks on mount
- Show badge count and glow state
- Render appropriate modal based on priority
- Track open/skip/complete events
- Auto-advance to next pending task

**State:**
```typescript
{
  isOpen: boolean,
  currentView: 'pulse' | 'feedback' | 'coaching' | 'chat',
  pendingTasks: PendingTask[],
  badge: number,
  glowState: 'none' | 'white' | 'red' | 'gold',
}
```

### 2. Unified Modal Container
**New Component:** `assistant-modal-container.component.ts`

**Props:**
- `view: 'pulse' | 'feedback' | 'coaching' | 'chat'`
- `onComplete: () => void`
- `onSkip: () => void`

**Renders:**
- Daily Pulse → `pulse-sheet` content
- Date Feedback → `feedback-prompt` content
- Weekly Coaching → `coaching-card` content
- Chat → `assistant-chat` content

### 3. Skip Button (Shared)
**Component:** Reusable skip button for all modals

**Visual:**
```
┌─────────────────────────┐
│   Modal Content         │
│                         │
│   [Primary Action]      │
│                         │
│   Skip ↗  (subtle)     │
└─────────────────────────┘
```

**Style:**
- Bottom-left or bottom-center
- Text link style (not prominent button)
- Color: `var(--text-dim)`
- Hover: `var(--text-muted)`

---

## Backend API

### New Endpoints

#### 1. Get Pending Tasks
```
GET /me/pending-tasks

Response:
{
  tasks: [
    {
      type: 'daily_pulse' | 'date_feedback' | 'weekly_coaching',
      priority: number,
      data: {
        // Type-specific data
        cycleId?: string,
        matchId?: string,
        partnerName?: string,
        summaryId?: number,
        summaryText?: string,
      }
    }
  ]
}
```

#### 2. Log Interaction Event
```
POST /me/interaction-log

Body:
{
  eventType: string,
  context: {
    cycleId?: string,
    matchId?: string,
    summaryId?: number,
    timeToComplete?: number,
    timeSpentReading?: number,
    nextAction?: string,
    deviceType?: string,
  }
}

Response:
{
  logged: true,
  eventId: number
}
```

#### 3. Updated Existing Endpoints
Add interaction logging to:
- `PUT /intake/dynamic` → log `DailyPulseCompleted`
- `POST /matches/{matchId}/feedback` → log `DateFeedbackCompleted`
- `POST /coaching/{id}/dismiss` → log `WeeklyCoachingDismissed`

---

## Database Schema

### New Table: `user_interaction_logs`
```sql
CREATE TABLE user_interaction_logs (
  id SERIAL PRIMARY KEY,
  user_id INT NOT NULL REFERENCES users(id),
  event_type VARCHAR(50) NOT NULL,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  context JSONB NOT NULL DEFAULT '{}',
  
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  
  INDEX idx_user_event (user_id, event_type),
  INDEX idx_occurred (occurred_at),
  INDEX idx_user_time (user_id, occurred_at)
);
```

### Event Types (Enum)
```sql
CREATE TYPE interaction_event_type AS ENUM (
  'DailyPulseCompleted',
  'DailyPulseSkipped',
  'DateFeedbackCompleted',
  'DateFeedbackSkipped',
  'WeeklyCoachingDismissed',
  'WeeklyCoachingSkipped',
  'AssistantChatOpened',
  'AssistantMessageSent'
);
```

---

## Implementation Plan

### Phase 1: Backend Foundation
1. Create `user_interaction_logs` table + migration
2. Create `IInteractionLogService` + `InteractionLogService`
3. Create `InteractionEndpoints` (`GET /me/pending-tasks`, `POST /me/interaction-log`)
4. Update existing endpoints to log completion events

### Phase 2: Frontend - Pending Tasks
5. Create `pending-task.service.ts` (fetch pending tasks)
6. Update `woven-assistant.component.ts` to check pending on mount
7. Implement badge count and glow state logic
8. Wire up priority sorting

### Phase 3: Frontend - Modal System
9. Create `assistant-modal-container.component.ts`
10. Move pulse/feedback/coaching into container
11. Add skip button to all modals
12. Implement auto-advance to next pending

### Phase 4: Frontend - Signal Tracking
13. Add timer tracking (timeToComplete, timeSpentReading)
14. Send completion events on save/submit
15. Send skip events on skip button click
16. Send chat-opened events

### Phase 5: Learning & Optimization
17. Create batch worker to analyze skip patterns
18. Implement timing optimization (don't show pulse on user's skip days)
19. Implement frequency optimization (pause features with >80% skip rate)
20. Add dashboard for product team to see engagement metrics

---

## Success Metrics

### Engagement
- **Completion Rate:** % of prompts completed vs skipped per feature
- **Time to Complete:** How long users spend on each task
- **Skip Rate:** % of users who skip vs complete

### Timing Optimization
- **Best Time:** When do users complete vs skip (by hour/day)
- **Skip Streaks:** How many consecutive skips before user stops engaging

### Feature Value
- **Pulse Engagement:** Daily Pulse completion rate
- **Feedback Quality:** Date Feedback completion rate + text length
- **Coaching Impact:** Weekly Coaching read time + dismiss rate

### Goal:
- Pulse: >60% completion rate
- Feedback: >70% completion rate (post-date)
- Coaching: >50% dismissal (vs skip) rate
- Assistant: >30% chat usage after task completion

---

## Open Questions

1. **Skip limit:** Auto-pause feature after X consecutive skips?
2. **Notification timing:** When to show badge (immediately vs wait for user to open app)?
3. **Mobile vs Desktop:** Different UX for mobile (bottom sheet) vs desktop (modal)?
4. **Sound/haptic:** Subtle notification when orb pulses?

---

## Future Enhancements

- **Smart scheduling:** ML model to predict best time to show each prompt per user
- **A/B testing:** Test different skip button placements/styles
- **Voice prompts:** "You have a quick check-in. Ready?" (audio cue)
- **Gesture shortcuts:** Swipe on orb to skip all, tap to open first
