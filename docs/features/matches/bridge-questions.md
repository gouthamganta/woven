# Bridge Questions

**Bridge questions** are ECHO-generated opening questions specific to each match pair. They appear on the Deck card after a user makes a choice, and are pre-loaded into the chat thread if a match is created.

---

## What is a bridge question?

**Definition:** A personalized opening question that the viewer could ask THIS specific candidate, rooted in real match data.

**Purpose:**
- Lowers barrier to starting conversation
- Grounds conversation in shared interests/aligned traits
- Invites a story or memory (not yes/no answer)
- Feels warm and curious, not interrogative

**Example:**
> "What's a place you've explored recently that surprised you?"

**Not this:**
> "Do you like traveling?" ❌ (generic, yes/no)

---

## Where bridge questions appear

### 1. Deck card (after choice)

**When:** User chooses MAGICAL or RESONANT on a Deck card  
**Where:** Shown in the "reason" section below the explanation

**UI mockup:**
```
┌─────────────────────────────────────────┐
│  You both love exploring cities on foot │
│  • Shared: photography, hiking, jazz    │
│                                         │
│  💬 "What's a place you've explored     │
│      recently that surprised you?"      │
│                                         │
│  [◈ Magical]  [◇ Resonant]  [Pass]     │
└─────────────────────────────────────────┘
```

**Note:** Bridge question is shown AFTER the choice is locked in (prevents gaming).

### 2. Chat thread (pre-loaded opener)

**When:** Match is created (mutual interest detected)  
**Where:** Pre-loaded as suggested message in chat input

**Frontend implementation:**
```typescript
// After match created
const bridgeQuestion = matchExplanation.bridgeQuestion;
if (bridgeQuestion) {
  this.chatService.setSuggestedMessage(threadId, bridgeQuestion);
}
```

**User sees:**
```
┌─────────────────────────────────────────┐
│  💬  Suggested:                         │
│  "What's a place you've explored        │
│   recently that surprised you?"         │
│                                         │
│  [Use this] or [Write your own]        │
└─────────────────────────────────────────┘
```

**User can:**
- Click "Use this" → sends bridge question as first message
- Click "Write your own" → clears suggestion, compose freely
- Ignore → suggestion stays visible until user types

---

## Generation requirements

**Hard requirements (enforced in OpenAI prompt):**

1. **Rooted in real match data**  
   Must reference a shared tag, aligned pillar, or hobby from PairContext

2. **Invites a story or memory**  
   Not yes/no. Not factual ("What's your favorite X?"). Open-ended.

3. **Max 20 words**  
   Short enough to feel conversational, not essay-prompt.

4. **Natural and warm**  
   Sounds like something a curious, genuine person would ask.

5. **Specific to this pair**  
   NOT generic ("What do you like to do?"). References their actual overlap.

---

## Good vs bad examples

| Shared data | Bad question ❌ | Good question ✅ |
|---|---|---|
| Photography | Do you like photography? | What's a photo you've taken that you're still proud of? |
| Jazz | What's your favorite jazz album? | What's a jazz album that stuck with you the first time you heard it? |
| Hiking | Do you hike often? | What's a trail you've hiked that surprised you with its views? |
| Cooking | Do you cook at home? | What's a dish you've cooked that felt like a small victory? |
| Intent: long-term | Are you looking for something serious? | What does a relationship that grows slowly and deliberately look like to you? |

**Why the good examples work:**
- They assume shared interest (no need to confirm)
- They invite a specific memory or story
- They signal curiosity, not interrogation
- They're conversational, not interview-style

---

## Generation logic

**Service:** `MatchExplanationService.cs` → `GenerateExplanationAsync()`

**Prompt snippet:**
```
Write 1 bridge question: an opening question the viewer could ask THIS specific person.
  * It must be rooted in something real from the match data (shared tag, aligned pillar, or hobby)
  * It should invite a story or memory, not a yes/no answer
  * Max 20 words
  * Sound like something a curious, warm person would genuinely ask
  * NOT generic ("what do you like to do?") — must be specific to this pair

Output JSON:
{
  "bridgeQuestion": "[Specific opening question for this pair]"
}
```

**Input data:**
```json
{
  "sharedTags": ["photography", "hiking", "jazz"],
  "alignedPillars": ["Energy", "Curiosity"],
  "sharedHobbies": ["weekend runs", "cooking"],
  "intentAlignment": "both want long-term, exclusive"
}
```

**Example output:**
```json
{
  "bridgeQuestion": "What's something you've gotten really into recently that surprised you?"
}
```

---

## Fallback bridge questions

**When used:**
- OpenAI generation fails
- No shared data available (rare)
- Parsing error

**Fallback by bucket:**

| Bucket | Fallback question |
|---|---|
| CORE_FIT | What's something you believed strongly that you've since changed your mind about? |
| LIFESTYLE_FIT | What does a genuinely good Saturday morning look like for you? |
| CONVERSATION_FIT | What's the last thing someone said that genuinely made you think differently? |
| EXPLORER | What's something you've gotten really into recently that surprised you? |
| WILDCARD | *(null — no bridge question)* |

**Note:** Fallback questions are still good openers, just not personalized to the pair.

---

## Database storage

**Table:** `match_explanations`

```sql
ALTER TABLE match_explanations
ADD COLUMN bridge_question VARCHAR(200) NULL;
```

**Migration:** `20260603000002_AddBridgeQuestionToMatchExplanation.cs`

**Entity:**
```csharp
public class MatchExplanation
{
    public int Id { get; set; }
    public int UserId { get; set; }         // Viewer
    public int CandidateId { get; set; }    // Match candidate
    public DateOnly DateUtc { get; set; }
    public string Headline { get; set; }
    public string BulletsJson { get; set; }
    public string Tone { get; set; }
    public string? DateIdea { get; set; }
    public string? DateIdeasJson { get; set; }
    public string? BridgeQuestion { get; set; }  // ← Added June 2026
    public DateTime CreatedAt { get; set; }
}
```

---

## API response format

**GET /moments/deck**

```json
{
  "items": [
    {
      "candidateId": 123,
      "fullName": "Alex",
      "profilePhoto": "https://...",
      "reason": {
        "headline": "You both love exploring cities on foot and talking for hours",
        "bullets": [
          "Shared interests: photography, hiking, jazz",
          "Aligned traits: you both value curiosity and spontaneity"
        ],
        "tone": "playful",
        "bridgeQuestion": "What's a place you've explored recently that surprised you?"
      }
    }
  ]
}
```

**GET /chats/{threadId}/suggested-opener** (if implemented)

```json
{
  "threadId": "abc-123",
  "suggestedMessage": "What's a place you've explored recently that surprised you?",
  "source": "bridge_question"
}
```

---

## Frontend implementation

**Deck page (show after choice):**

```typescript
// moments.page.ts
interface MomentReason {
  headline: string;
  bullets: string[];
  tone: string;
  bridgeQuestion?: string | null;
}

// In template
<div class="bridge-question" *ngIf="moment.reason?.bridgeQuestion">
  <div class="icon">💬</div>
  <div class="text">"{{ moment.reason.bridgeQuestion }}"</div>
</div>
```

**Chat thread (pre-load opener):**

```typescript
// chat-thread.component.ts
async ngOnInit() {
  const threadId = this.route.snapshot.paramMap.get('threadId');
  
  // Load match explanation
  const explanation = await this.moments.getExplanationForThread(threadId);
  
  if (explanation?.bridgeQuestion && !this.hasMessages) {
    this.suggestedMessage = explanation.bridgeQuestion;
  }
}

useSuggestedMessage() {
  this.messageText = this.suggestedMessage;
  this.suggestedMessage = null;
}

dismissSuggestion() {
  this.suggestedMessage = null;
}
```

---

## Behavioral signal tracking

**When user sends bridge question as first message:**

```csharp
// MatchSignalService.RecordAsync()
await _signals.RecordAsync(new MatchSignalLog
{
    ViewerId = senderId,
    CandidateId = recipientId,
    EventType = "BridgeQuestionSent",
    EventValue = 1,
    MetadataJson = JsonSerializer.Serialize(new
    {
        threadId,
        messageId,
        question = bridgeQuestion
    }),
    OccurredAt = DateTimeOffset.UtcNow
});
```

**ECHO learns:**
- Do bridge questions increase first-message send rate?
- Do they decrease time-to-first-message?
- Which types of questions (hobby-based vs pillar-based) work best?

---

## Quality metrics

**How we measure bridge question quality:**

1. **Usage rate** — % of users who send bridge question as first message
2. **Response rate** — % of recipients who respond to bridge question
3. **Response depth** — avg character count of responses (story vs one-word)
4. **Time to response** — do bridge questions get faster replies?

**Logged signals:**
- `BridgeQuestionSent` — user sent the pre-loaded question
- `BridgeQuestionIgnored` — user composed own first message
- `TimeToFirstResponseMs` — how long until recipient replied

---

## Edge cases

**What if no shared data exists?**
- Rare (deck selection ensures some overlap)
- Fallback to bucket-appropriate generic question

**What if bridge question is null?**
- Frontend hides suggestion UI
- User composes own opener

**What if user ignores bridge question?**
- Suggestion stays visible until user types
- After user types 5+ characters, suggestion auto-dismisses

**What if bridge question is too long?**
- OpenAI prompt enforces 20-word max
- Backend validation trims to 200 chars
- Frontend ellipsis if needed

---

## Future enhancements

**Multiple bridge questions:**
- Generate 3 bridge questions per pair
- User picks which one to send
- A/B test which style performs best

**Bridge question templates:**
- Pre-defined templates per pillar/tag
- Fill-in-the-blank approach for speed
- Hybrid: template + AI refinement

**User-submitted bridge questions:**
- Users can save favorite openers
- ECHO learns which questions work for this user
- Personalized bridge question library

---

## Writing guidelines (for ECHO)

**Do:**
- Assume shared interest (no need to confirm)
- Ask for a specific memory or story
- Use "What's a..." or "What's something..." framing
- Reference their actual data (tag, hobby, pillar)
- Sound warm and curious

**Don't:**
- Ask yes/no questions
- Use "Do you..." or "Are you..." framing
- Ask generic questions ("What do you like?")
- Ask factual trivia ("What's your favorite X?")
- Sound like an interview or interrogation

**Examples of good framing:**

| Instead of... | Try... |
|---|---|
| Do you like X? | What about X caught your interest? |
| What's your favorite X? | What's an X that stuck with you? |
| Are you into X? | What's your story with X? |
| Tell me about X | What's a moment when X surprised you? |

---

## Related files

**Backend:**
- `backend/WovenBackend/Services/Matchmaking/MatchExplanationService.cs` — Generation
- `backend/WovenBackend/Data/Entities/MatchExplanation.cs` — Entity model
- `backend/WovenBackend/Endpoints/MomentsEndpoints.cs` — Deck API

**Frontend:**
- `frontend/woven-frontend/src/app/pages/moments/moments.page.ts` — Deck display
- `frontend/woven-frontend/src/app/pages/chats/chat-thread.component.ts` — Pre-load
- `frontend/woven-frontend/src/app/services/moments.service.ts` — API client

**Database:**
- Migration: `20260603000002_AddBridgeQuestionToMatchExplanation.cs`
- Table: `match_explanations` (column: `bridge_question`)

---

## Testing checklist

**Generation:**
- [ ] Bridge question references at least 1 shared tag, pillar, or hobby
- [ ] Question invites story/memory (not yes/no)
- [ ] Max 20 words
- [ ] Sounds natural and warm
- [ ] Not generic (specific to this pair)

**Display:**
- [ ] Bridge question shown on Deck card after choice
- [ ] Question is quoted and visually distinct
- [ ] Hidden if null
- [ ] Pre-loaded into chat thread if match created

**Behavior:**
- [ ] User can send bridge question as first message (one click)
- [ ] User can dismiss suggestion
- [ ] Suggestion auto-dismisses after user types
- [ ] Signal logged when bridge question sent

**Fallback:**
- [ ] Fallback question used when OpenAI fails
- [ ] Fallback is bucket-appropriate
- [ ] Deck card still shows bridge question (not blank)
