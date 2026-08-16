# Woven — System Flowcharts

All diagrams use Mermaid syntax. Render with any Mermaid-compatible viewer (GitHub, GitLab, Obsidian, mermaid.live).

---

## Flowchart 1: User Lifecycle

```mermaid
flowchart TD
    A([App opened]) --> B[Google OAuth]
    B --> C{Existing account?}
    C -- No --> D[Onboarding: Welcome]
    C -- Yes --> Z

    D --> E[Onboarding: Basics\nname, gender, orientation]
    E --> F[Onboarding: Intent\nwhat they are looking for]
    F --> G[Onboarding: Foundational questions\ncore pillar responses]
    G --> H[Onboarding: Photos\nupload 1–6 photos]
    H --> I[Onboarding: Details\nheight, occupation, etc.]
    I --> J[Onboarding: Lifestyle\nhabits, values]
    J --> K[Onboarding: Review\npreview profile]
    K --> L[Onboarding: Start\naccount activated]

    L --> Z[Deck — daily candidate cards]

    Z --> M{User responds to card}
    M -- PASS --> Z
    M -- MAGICAL or RESONANT --> N[Submit note\n20–150 chars required]
    N --> O{Counterpart also responded\npositively and submitted note?}
    O -- No, waiting --> Z
    O -- Yes --> P[Match created\nchat thread unlocked\nboth notified]

    P --> Q{Both users opened chat?}
    Q -- Not yet --> Q
    Q -- Yes: 2nd user opens --> R[TrialEndsAt = now + 3 min\nTrial window active]

    R --> S{Trial decision}
    S -- CONTINUE --> T[Trial marked continued\nmatch stays open]
    S -- END --> U[Match closed\nend reason stored\nghost refund if no messages sent]
    S -- BLOCK --> V[Match closed\nBlock record created]
    S -- Expired, no decision --> U

    T --> W{Both users sent a message?}
    W -- No --> T
    W -- Yes: BothMessagedAt set --> X[FindLoveAt = BothMessagedAt + 5 min]

    X --> Y[3 date idea cards appear]
    Y --> AA[User selects Plan It\nDateIdeaAccepted signal logged]
    AA --> AB{Counterpart also selected?}
    AB -- No, waiting --> AB
    AB -- Yes --> AC([Mutual interest notification\nFind Love complete])
```

---

## Flowchart 2: ECHO ML Pipeline

```mermaid
flowchart TD
    A([User action in app]) --> B[IMatchSignalService.RecordAsync\nviewerId, candidateId, eventType, eventValue]
    B --> C[(MatchSignalLogs\nappend-only ledger)]

    C --> D[ConnectionScoreBatchWorker\nruns 03:50 UTC daily]

    D --> E[Weighted composite score\nper viewer–candidate pair]
    E --> F{Score >= 0.08\nminimum threshold?}
    F -- No: pair skipped --> G([Pair excluded from training])
    F -- Yes --> H[(ConnectionScore stored)]

    H --> I[WeightLearningBatchWorker\nruns Sunday 04:00 UTC]
    I --> J{User has >= 10\nqualifying pairs?}
    J -- No --> K([User keeps default weights\nfrom appsettings.json])
    J -- Yes --> L[Logistic regression\non ConnectionScores]
    L --> M[(UserMatchingWeights updated)]

    M --> N[DailyDeckOrchestrator\ngenerates deck with learned weights]
    N --> O[BehavioralFingerprintService\n16-dim fingerprint, 180-day window]
    O --> P[DeliveryBoostService\n12-step boost pipeline applied]
    P --> Q([Deck served to user])

    subgraph ConnectionScore weights
        W1[BalloonPopped: 0.05]
        W2[TrialRequested: 0.10]
        W3[TrialAccepted: 0.25]
        W4[ConversationDepth: 0.20]
        W5[DateAccepted: 0.15]
        W6[ExplicitFeedback: 0.15]
        W7[LoveReactions: 0.10]
    end
```

---

## Flowchart 3: Balloon Pop — Match Creation

```mermaid
flowchart TD
    A([User views candidate card\nGET /moments]) --> B{Which deck?}
    B -- Deck tab: today --> C[Response is free]
    B -- Drawn tab: liked-you --> D[Response costs 1 spark\ndeducted from wallet]

    C --> E[POST /moments/respond\nMAGICAL or RESONANT]
    D --> E

    E --> F{alreadyChoseYou check:\ndid candidate already respond\npositively to viewer?}

    F -- No: candidate has not responded yet --> G[Response recorded\nwaiting for counterpart]
    G --> Z([Flow paused — counterpart must respond])

    F -- Yes: mutual positive responses --> H[POST /moments/choose\nnoteText required, 20–150 chars]

    H --> I{Note valid?}
    I -- No: too short or too long --> J([Error returned, user re-enters note])
    I -- Yes --> K[Match created\nBalloonState = ACTIVE\nchat thread created]

    K --> L[Both users notified]
    L --> M([Match visible in chat list\nBalloon is open])

    G --> N{Counterpart later submits\ntheir response and note}
    N --> K
```

---

## Flowchart 4: Trial Period

```mermaid
flowchart TD
    A([Match created\nBalloonState = ACTIVE]) --> B[User A opens chat thread]
    B --> C[TrialUserAOpenedAt = now]
    C --> D{Has User B also opened?}

    D -- No: waiting for B --> E[User B opens chat thread]
    E --> F[TrialUserBOpenedAt = now]
    F --> G[TrialEndsAt = now + 3 minutes\nTrial window begins]

    D -- Yes: B already opened --> G

    G --> H{Trial decision made\nbefore TrialEndsAt?}

    H -- CONTINUE --> I[IsTrial marked continued\nMatch stays open\nNo expiry]

    H -- END --> J[End reason required:\nno_spark / wrong_timing / not_my_type]
    J --> K[TrialEndReason stored\nfeeds ECHO signal pipeline]
    K --> L{Any messages exchanged?}
    L -- No: neither user sent a message --> M[Ghost refund: 0.5 sparks\nreturned to responding user]
    L -- Yes --> N[No refund]
    M --> O([Match closed\nBalloonState = CLOSED])
    N --> O

    H -- BLOCK --> P[Match closed immediately\nBlock record created]
    P --> O

    H -- No decision before TrialEndsAt --> Q[Auto-close fires]
    Q --> L
```

---

## Flowchart 5: Find Love Flow

```mermaid
flowchart TD
    A([Trial: CONTINUE decision made\nmatch stays open]) --> B{Both users sent\nat least one message?}

    B -- No --> C[Chat continues\nBothMessagedAt not yet set]
    C --> B

    B -- Yes: second user's first message sent --> D[BothMessagedAt = now]
    D --> E[FindLoveAt = BothMessagedAt + 5 minutes\nReflectionWindow countdown]

    E --> F{Current time >= FindLoveAt?}
    F -- No: window not elapsed --> G[Normal chat continues]
    G --> F

    F -- Yes --> H[3 date idea cards appear\ngenerated by MatchExplanationService]

    H --> I{User selects a date idea}
    I -- Plan It tapped --> J[POST to date selection endpoint\nDateIdeaAccepted signal logged\nvia IMatchSignalService.RecordAsync]

    J --> K{Has counterpart also\nselected a date idea?}
    K -- No: waiting --> L[Selection stored\nwaiting for counterpart]
    L --> K

    K -- Yes: both selected --> M[Mutual interest notification\nsent to both users]
    M --> N([Find Love complete])
```
