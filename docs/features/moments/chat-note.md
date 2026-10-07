# ChatNote Overlay — Opening Note System

**File:** `frontend/woven-frontend/src/app/pages/moments/chat-note-overlay.component.ts`  
**Endpoint:** `POST /moments/choose`  
**Table:** `chat_notes`

## Overview

The ChatNote overlay appears after a user chooses Magical ◈ or Resonant ◇ (not Pass). It requires a 20-150 character opening note before the choice is submitted. This note is a **background signal only** — it's never shown to the other user, only fed to ECHO for preference learning.

## When shown

**Deck tab:**
- User taps Magical or Resonant → 420ms flash animation → overlay opens

**Drawn tab:**
- User taps Magical or Resonant → 420ms flash animation → overlay opens

**Not shown:**
- Pass action (no overlay, no note)

## Overlay structure

**Component:** `ChatNoteOverlayComponent` ([chat-note-overlay.component.ts:25-377](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\chat-note-overlay.component.ts#L25-L377))

### Input props

```typescript
@Input() card: OverlayCard | null = null;
@Input() choice: 'MAGICAL' | 'LOGICAL' | null = null;

export type OverlayCard = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
  bridgeQuestion?: string | null;  // Only populated on Deck tab
};
```

### Output events

```typescript
@Output() back = new EventEmitter<void>();        // User tapped back button
@Output() submitted = new EventEmitter<string>(); // Note submitted (min 20 chars)
```

## Layout sections

### 1. Photo area (50dvh)

**Elements:**
- Back button (top-left)
- Profile photo or fallback (first initial)
- Bottom gradient with name + choice badge

**Choice badge styling:**
- **Magical:** Gold background (`rgba(212,160,23,0.18)`), gold border, "◈ Magical" label
- **Resonant:** Rose background (`rgba(220,80,80,0.14)`), rose border, "◇ Resonant" label

### 2. Writing area (flex: 1, scrollable)

**Elements:**
1. Bridge question suggestion (Deck tab only)
2. Starter dropdown ("Need a starter?")
3. Textarea (20-150 chars, 4 rows min)
4. Meta row: "20 more to go" hint + char count
5. Send button (disabled until 20 chars)

## Bridge question (Deck tab only)

**What it is:** A question suggested by ECHO to help start conversation, based on shared interests or compatibility signals.

**Example:** "What's your favorite way to unwind after a long week?"

**When shown:**
- Deck tab cards may have `reason.bridgeQuestion` (populated by `MatchExplanationService`)
- Drawn tab cards always have `bridgeQuestion = null`

**Display** ([chat-note-overlay.component.ts:60-65](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\chat-note-overlay.component.ts#L60-L65)):
```html
<div class="bridgeWrap" *ngIf="card!.bridgeQuestion">
  <span class="bridgeLabel">Try asking</span>
  <button class="bridgeQuestion" (click)="applyBridgeQuestion()">
    {{ card!.bridgeQuestion }}
  </button>
</div>
```

**One-click apply** ([chat-note-overlay.component.ts:350-355](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\chat-note-overlay.component.ts#L350-L355)):
```typescript
applyBridgeQuestion() {
  if (this.card?.bridgeQuestion) {
    this.noteText = this.card.bridgeQuestion;
    this.cdr.markForCheck();
  }
}
```

## Starter dropdown

**Purpose:** Help users who are stuck staring at blank textarea.

**Starters** ([chat-note-overlay.component.ts:19-23](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\chat-note-overlay.component.ts#L19-L23)):
```typescript
const STARTERS = [
  'Your photo made me want to say…',
  'I\'d love to talk about…',
  'Something tells me we\'d…',
];
```

**Interaction:**
1. User taps "Need a starter?" → dropdown opens
2. User taps a starter → fills textarea, closes dropdown
3. User can edit from there

**Code** ([chat-note-overlay.component.ts:357-361](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\chat-note-overlay.component.ts#L357-L361)):
```typescript
applyStarter(s: string) {
  this.noteText = s;
  this.showDropdown = false;
  this.cdr.markForCheck();
}
```

## Textarea validation

**Rules:**
- **Min:** 20 characters (trimmed)
- **Max:** 150 characters (hard limit via `maxlength` attribute)

**Visual feedback:**
- **< 20 chars:** "20 more to go" hint shown, Send button disabled
- **130-150 chars:** Char count turns red (`.warn` class)
- **≥ 20 chars:** Send button enabled

**Code** ([chat-note-overlay.component.ts:339-341](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\chat-note-overlay.component.ts#L339-L341)):
```typescript
get canSend(): boolean {
  return this.noteText.trim().length >= 20;
}
```

## Send button

**Styling:**
- **Magical:** Gold gradient (`linear-gradient(135deg, var(--gold-500), var(--gold-400))`), dark text, gold glow shadow
- **Resonant:** Rose gradient (`linear-gradient(135deg, var(--rose-500), var(--rose-400))`), white text, rose glow shadow

**States:**
- **Disabled:** `opacity: 0.35`, `cursor: not-allowed` (< 20 chars or submitting)
- **Enabled:** Full opacity, hover darkens slightly
- **Active (pressed):** `transform: scale(0.96)` (haptic feedback)

**Submit handler** ([chat-note-overlay.component.ts:373-376](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\chat-note-overlay.component.ts#L373-L376)):
```typescript
onSend() {
  if (!this.canSend || this.submitting) return;
  this.submitted.emit(this.noteText.trim());
}
```

## Parent component handling

**On submit** ([moments.page.ts:347-393](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L347-L393)):
```typescript
async onOverlaySubmit(noteText: string) {
  if (!this.overlayCard || !this.overlayChoice) return;

  const { userId } = this.overlayCard;
  const choice = this.overlayChoice;
  const source = this.overlaySource;
  const timeOnCardMs = this.overlayTimeOnCardMs;

  // Optimistically hide and close overlay
  if (source === 'TODAY') {
    this.respondedUserIds.add(userId);
    this.checkDeckCompletion();
  } else {
    this.respondedLikedYouIds.add(userId);
  }
  this.overlayCard = null;
  this.overlayChoice = null;
  this.cdr.markForCheck();

  try {
    const res = await firstValueFrom(
      this.moments.choose({ targetUserId: userId, choice, noteText, source, timeOnCardMs })
    );

    if (res?.sparkBalance != null) this.sparkBalance = res.sparkBalance;

    if (res?.matchId && (res.status === 'PURE_MATCH_CREATED' || res.status === 'EDGE_MATCH_CREATED')) {
      const copy = this.matchRevealCopy(res.matchType ?? '', res.status);
      this.showToast(copy);
      const started = await firstValueFrom(this.chat.start(res.matchId));
      this.router.navigateByUrl(`/chats/${started.threadId}`);
      return;
    }

    if (res.status === 'WAITING_FOR_OTHER_NOTE') {
      this.showToast(choice === 'MAGICAL' ? '◈ Sent — waiting for them' : '◇ Sent — waiting for them');
    } else {
      this.showToast(choice === 'MAGICAL' ? '◈ Sent' : '◇ Sent');
    }
  } catch {
    if (source === 'TODAY') this.respondedUserIds.delete(userId);
    else this.respondedLikedYouIds.delete(userId);
    this.showToast('Something went wrong. Try again.');
  } finally {
    this.cdr.markForCheck();
  }
}
```

**Optimistic UI:** Card disappears immediately, overlay closes. If API call fails, card reappears + error toast.

## Backend flow

**Endpoint:** `POST /moments/choose` ([MomentsEndpoints.cs:664-871](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Endpoints\MomentsEndpoints.cs#L664-L871))

### Request validation

```csharp
public record ChooseRequest(int TargetUserId, string Choice, string NoteText, string? Source, int? TimeOnCardMs);

var noteText = (req.NoteText ?? "").Trim();
if (noteText.Length < 20 || noteText.Length > 150)
    return Results.BadRequest(new { error = "NOTE_LENGTH_INVALID", min = 20, max = 150 });
```

### Budget/spark spend

**Deck tab (`source = "TODAY"`):**
```csharp
var spend = await budget.TrySpendAsync(me, InteractionBudgetService.SpendType.Moment, ct);
if (!spend.Allowed)
    return Results.BadRequest(new { error = spend.DenyReason, spend.TotalUsed });
```

**Drawn tab (`source = "LIKED_YOU"`):**
```csharp
var sparkSpend = await sparks.TrySpendAsync(me, ct);
if (!sparkSpend.Allowed)
    return Results.BadRequest(new { error = sparkSpend.DenyReason, sparkBalance = sparkSpend.Balance });
```

### Record MomentResponse + ChatNote

```csharp
// Upsert MomentResponse (may be upgrading from PASS)
if (existingResponse == null)
{
    db.MomentResponses.Add(new MomentResponse
    {
        DateUtc = today,
        FromUserId = me,
        ToUserId = req.TargetUserId,
        Choice = choiceEnum.Value,
        TimeOnCardMs = req.TimeOnCardMs,
        Source = source,
        CreatedAt = MomentsRules.NowUtc()
    });
}

// Record ChatNote
var note = new ChatNote
{
    FromUserId = me,
    ToUserId = req.TargetUserId,
    Choice = choiceEnum.Value,
    NoteText = noteText,
    Source = source,
    CreatedAt = MomentsRules.NowUtc()
};
db.ChatNotes.Add(note);
await db.SaveChangesAsync(ct);
```

### Check for match

**Requires both:**
1. Counterpart `MomentResponse` with positive choice (MAGICAL/LOGICAL)
2. Counterpart `ChatNote` exists

```csharp
// Check counterpart response
var otherResponse = await db.MomentResponses.AsNoTracking()
    .FirstOrDefaultAsync(r =>
        r.DateUtc == today &&
        r.FromUserId == req.TargetUserId &&
        r.ToUserId == me &&
        IsPositiveExpression(r.Choice), ct);

if (otherResponse is null)
    return Results.Ok(new { status = "RECORDED_WAITING" });

// Check counterpart note
var otherNote = await db.ChatNotes.AsNoTracking()
    .FirstOrDefaultAsync(n => n.FromUserId == req.TargetUserId && n.ToUserId == me, ct);

if (otherNote is null)
    return Results.Ok(new { status = "WAITING_FOR_OTHER_NOTE" });

// Both exist → create match
```

See `match-creation.md` for full match creation logic.

## ChatNote entity

**Table:** `chat_notes` ([ChatNote.cs](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\data\Entities\Moments\ChatNote.cs)):
```csharp
[Table("chat_notes")]
public class ChatNote
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("from_user_id")]
    public int FromUserId { get; set; }

    [Column("to_user_id")]
    public int ToUserId { get; set; }

    [Column("choice")]
    public MomentChoice Choice { get; set; }  // MAGICAL or LOGICAL

    [Column("note_text")]
    [MaxLength(150)]
    public string NoteText { get; set; } = "";

    [Column("source")]
    [MaxLength(20)]
    public string? Source { get; set; }  // "TODAY" or "LIKED_YOU"

    [Column("match_id")]
    public Guid? MatchId { get; set; }  // Linked after match created

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

**Key columns:**
- `note_text` — User's opening note (never shown to other user)
- `match_id` — Links to `matches` table after match created
- `source` — Tracks where choice was made (analytics)

## Privacy note

**ChatNotes are background signals only.** They are:
- Never displayed to the other user
- Never displayed in chat thread
- Used only for ECHO preference learning (future feature)
- Stored in DB for potential future analysis

**Rationale:** Users write more honestly when they know the note won't be shown verbatim. The goal is to capture *why* they're interested (e.g., "love your hiking photos" → ECHO learns viewer values outdoor lifestyle), not to create a messaging burden.

## Related files

**Frontend:**
- `frontend/woven-frontend/src/app/pages/moments/chat-note-overlay.component.ts` — Overlay component
- `frontend/woven-frontend/src/app/pages/moments/moments.page.ts:347-393` — Parent submit handler

**Backend:**
- `backend/WovenBackend/Endpoints/MomentsEndpoints.cs:664-871` — `POST /moments/choose`
- `backend/WovenBackend/data/Entities/Moments/ChatNote.cs` — Entity
