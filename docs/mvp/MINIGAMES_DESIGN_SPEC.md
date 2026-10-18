# Mini-Games Design Specification
**Version:** 1.0  
**Date:** 2026-10-18  
**Status:** LOCKED

---

## Design Philosophy

**Principles:**
- **Simple > Complex** - No complex animations, just clean interactions
- **Consistent** - Match existing Woven design system exactly
- **Mobile-first** - Works perfectly on phone screens
- **NGL-inspired** - Clean card-based UI, minimal, shareable

**No:**
- ❌ Complex motion animations
- ❌ Hover translateY lifts
- ❌ Gratuitous effects
- ❌ External animation libraries

**Yes:**
- ✅ Simple fades, slides (CSS transitions)
- ✅ Clean card layouts
- ✅ Existing design tokens
- ✅ Touch-friendly interactions

---

## Design System (Reuse Existing)

### Colors (Use Existing Tokens)

```scss
// From existing Woven design system
--primary: #6C63FF;
--secondary: #FF6584;
--success: #4CAF50;
--warning: #FFC107;
--error: #F44336;

--bg: #FAFAFA;
--card-bg: #FFFFFF;
--border: #E0E0E0;

--text-primary: #212121;
--text-secondary: #757575;
--text-tertiary: #9E9E9E;

// Dark mode
[data-theme="dark"] {
  --bg: #121212;
  --card-bg: #1E1E1E;
  --border: #333333;
  --text-primary: #FFFFFF;
  --text-secondary: #B0B0B0;
}
```

### Typography (Use Existing)

```scss
// Headings
h1 { font-size: 28px; font-weight: 700; }
h2 { font-size: 24px; font-weight: 600; }
h3 { font-size: 20px; font-weight: 600; }

// Body
body { font-size: 16px; line-height: 1.5; }
.subtitle { font-size: 14px; color: var(--text-secondary); }
.caption { font-size: 12px; color: var(--text-tertiary); }
```

### Spacing (Use Existing)

```scss
--spacing-xs: 4px;
--spacing-sm: 8px;
--spacing-md: 16px;
--spacing-lg: 24px;
--spacing-xl: 32px;
```

---

## Component Designs

### 1. Game Card (Base Template)

```html
<div class="game-card">
  <div class="game-header">
    <div class="progress">3/6</div>
    <h3>{{ gameTitle }}</h3>
  </div>
  
  <div class="game-content">
    <!-- Game-specific content -->
  </div>
  
  <button class="primary-btn">Next</button>
</div>
```

**Styles:**
```scss
.game-card {
  background: var(--card-bg);
  border-radius: 16px;
  padding: 24px;
  max-width: 400px;
  margin: 0 auto;
  box-shadow: 0 2px 8px rgba(0,0,0,0.08);
  
  .game-header {
    margin-bottom: 24px;
    
    .progress {
      font-size: 14px;
      color: var(--text-secondary);
      margin-bottom: 8px;
    }
    
    h3 {
      font-size: 20px;
      font-weight: 600;
      line-height: 1.4;
    }
  }
  
  .primary-btn {
    width: 100%;
    padding: 16px;
    background: var(--primary);
    color: white;
    border: none;
    border-radius: 12px;
    font-size: 16px;
    font-weight: 600;
    margin-top: 24px;
    cursor: pointer;
    transition: opacity 0.2s;
    
    &:hover {
      opacity: 0.9;
    }
    
    &:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
  }
}
```

### 2. Progress Indicator

```html
<div class="game-progress">
  <div class="progress-text">Question 3 of 6</div>
  <div class="progress-bar">
    <div class="progress-fill" [style.width]="progress + '%'"></div>
  </div>
</div>
```

**Styles:**
```scss
.game-progress {
  margin-bottom: 24px;
  
  .progress-text {
    font-size: 14px;
    color: var(--text-secondary);
    margin-bottom: 8px;
  }
  
  .progress-bar {
    height: 4px;
    background: var(--border);
    border-radius: 2px;
    overflow: hidden;
    
    .progress-fill {
      height: 100%;
      background: var(--primary);
      transition: width 0.3s ease;
    }
  }
}
```

### 3. Slider Game

```html
<div class="slider-game">
  <h3>{{ question }}</h3>
  
  <div class="slider-container">
    <input 
      type="range" 
      min="0" 
      max="100" 
      [(ngModel)]="value"
      class="slider"
    />
    <div class="slider-labels">
      <span>{{ leftLabel }}</span>
      <span>{{ rightLabel }}</span>
    </div>
  </div>
</div>
```

**Styles:**
```scss
.slider-container {
  padding: 32px 0;
  
  .slider {
    width: 100%;
    height: 6px;
    background: var(--border);
    border-radius: 3px;
    outline: none;
    
    &::-webkit-slider-thumb {
      appearance: none;
      width: 24px;
      height: 24px;
      background: var(--primary);
      border-radius: 50%;
      cursor: pointer;
    }
  }
  
  .slider-labels {
    display: flex;
    justify-content: space-between;
    margin-top: 12px;
    font-size: 14px;
    color: var(--text-secondary);
  }
}
```

### 4. Ranking Game (Drag-Drop)

```html
<div class="ranking-game">
  <h3>Rank these by importance</h3>
  
  <div cdkDropList class="ranking-list">
    <div 
      *ngFor="let item of items; let i = index"
      cdkDrag
      class="rank-item"
    >
      <span class="rank-number">{{ i + 1 }}</span>
      <span class="rank-label">{{ item }}</span>
      <span class="drag-handle">⋮⋮</span>
    </div>
  </div>
</div>
```

**Styles:**
```scss
.ranking-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  
  .rank-item {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 16px;
    background: var(--bg);
    border: 1px solid var(--border);
    border-radius: 12px;
    cursor: move;
    transition: transform 0.2s;
    
    &:active {
      transform: scale(1.02);
    }
    
    .rank-number {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 28px;
      height: 28px;
      background: var(--primary);
      color: white;
      border-radius: 50%;
      font-size: 14px;
      font-weight: 600;
    }
    
    .rank-label {
      flex: 1;
      font-size: 15px;
    }
    
    .drag-handle {
      color: var(--text-tertiary);
      cursor: grab;
    }
  }
}
```

### 5. Swipe Cards

```html
<div class="swipe-game">
  <h3>{{ question }}</h3>
  
  <div class="card-stack">
    <div class="swipe-card" *ngFor="let option of options; let i = index"
         [class.active]="i === currentIndex">
      <p>{{ option.text }}</p>
    </div>
  </div>
  
  <div class="swipe-actions">
    <button class="swipe-btn nope" (click)="swipe('left')">
      👎 Nope
    </button>
    <button class="swipe-btn yes" (click)="swipe('right')">
      👍 Yes!
    </button>
  </div>
</div>
```

**Styles:**
```scss
.card-stack {
  position: relative;
  height: 200px;
  margin: 24px 0;
  
  .swipe-card {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    padding: 32px;
    background: var(--card-bg);
    border: 2px solid var(--border);
    border-radius: 16px;
    text-align: center;
    opacity: 0;
    transform: translateX(-20px);
    transition: opacity 0.3s, transform 0.3s;
    
    &.active {
      opacity: 1;
      transform: translateX(0);
    }
    
    p {
      font-size: 16px;
      line-height: 1.6;
    }
  }
}

.swipe-actions {
  display: flex;
  gap: 12px;
  
  .swipe-btn {
    flex: 1;
    padding: 14px;
    border-radius: 12px;
    font-size: 15px;
    font-weight: 600;
    border: none;
    cursor: pointer;
    transition: opacity 0.2s;
    
    &:hover {
      opacity: 0.9;
    }
    
    &.nope {
      background: #F5F5F5;
      color: var(--text-primary);
    }
    
    &.yes {
      background: var(--primary);
      color: white;
    }
  }
}
```

### 6. Selection Grid

```html
<div class="selection-game">
  <h3>{{ question }}</h3>
  <p class="subtitle">Pick {{ maxSelect }}</p>
  
  <div class="selection-grid">
    <div 
      *ngFor="let option of options"
      class="selection-item"
      [class.selected]="isSelected(option)"
      (click)="toggle(option)"
    >
      <div class="icon">{{ option.icon }}</div>
      <div class="label">{{ option.label }}</div>
    </div>
  </div>
</div>
```

**Styles:**
```scss
.selection-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(100px, 1fr));
  gap: 12px;
  margin-top: 16px;
  
  .selection-item {
    padding: 20px 12px;
    background: var(--bg);
    border: 2px solid var(--border);
    border-radius: 12px;
    text-align: center;
    cursor: pointer;
    transition: all 0.2s;
    
    &:hover {
      border-color: var(--primary);
    }
    
    &.selected {
      background: var(--primary);
      border-color: var(--primary);
      color: white;
      
      .icon {
        transform: scale(1.1);
      }
    }
    
    .icon {
      font-size: 32px;
      margin-bottom: 8px;
      transition: transform 0.2s;
    }
    
    .label {
      font-size: 13px;
      font-weight: 500;
    }
  }
}
```

---

## Results Page Design

### Rich Personality Card

```html
<div class="results-card">
  <div class="personality-badge">
    <h2>You're a Curious Explorer 🌟</h2>
  </div>
  
  <div class="personality-description">
    <ul>
      <li>You thrive in deep conversations that challenge your worldview</li>
      <li>You value personal growth over material comfort</li>
      <li>You're energized by new ideas and spontaneous adventures</li>
      <li>You prefer authenticity over surface-level connections</li>
    </ul>
  </div>
  
  <div class="pillar-breakdown">
    <h3>Your 8 Pillars</h3>
    <div class="pillar-bar" *ngFor="let pillar of pillars">
      <div class="pillar-label">
        <span class="icon">{{ pillar.icon }}</span>
        <span class="name">{{ pillar.name }}</span>
      </div>
      <div class="bar-container">
        <div class="bar-fill" [style.width]="pillar.score + '%'"></div>
      </div>
      <div class="score">{{ pillar.score }}%</div>
    </div>
  </div>
  
  <div class="result-code">
    <p>Your code: <strong>CE-8K2P</strong></p>
  </div>
</div>
```

**Styles:**
```scss
.results-card {
  background: var(--card-bg);
  border-radius: 16px;
  padding: 32px;
  max-width: 500px;
  margin: 0 auto;
  
  .personality-badge {
    text-align: center;
    margin-bottom: 24px;
    
    h2 {
      font-size: 24px;
      font-weight: 700;
      background: linear-gradient(135deg, var(--primary), var(--secondary));
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
    }
  }
  
  .personality-description {
    margin-bottom: 32px;
    
    ul {
      list-style: none;
      padding: 0;
      
      li {
        padding: 12px 0;
        padding-left: 28px;
        position: relative;
        line-height: 1.6;
        
        &:before {
          content: "•";
          position: absolute;
          left: 8px;
          color: var(--primary);
          font-weight: bold;
        }
      }
    }
  }
  
  .pillar-breakdown {
    margin-bottom: 24px;
    
    h3 {
      font-size: 18px;
      margin-bottom: 16px;
    }
    
    .pillar-bar {
      display: grid;
      grid-template-columns: 140px 1fr 50px;
      align-items: center;
      gap: 12px;
      margin-bottom: 12px;
      
      .pillar-label {
        display: flex;
        align-items: center;
        gap: 8px;
        font-size: 14px;
        
        .icon {
          font-size: 18px;
        }
      }
      
      .bar-container {
        height: 8px;
        background: var(--border);
        border-radius: 4px;
        overflow: hidden;
        
        .bar-fill {
          height: 100%;
          background: linear-gradient(90deg, var(--primary), var(--secondary));
          border-radius: 4px;
          transition: width 0.5s ease;
        }
      }
      
      .score {
        font-size: 14px;
        font-weight: 600;
        text-align: right;
      }
    }
  }
  
  .result-code {
    text-align: center;
    padding: 16px;
    background: var(--bg);
    border-radius: 12px;
    
    p {
      font-size: 14px;
      color: var(--text-secondary);
      
      strong {
        color: var(--primary);
        font-size: 18px;
        letter-spacing: 1px;
      }
    }
  }
}
```

### Compatibility Card

```html
<div class="compatibility-card">
  <h3>Your match with the person who shared this</h3>
  
  <div class="compatibility-score">
    <div class="score-circle">
      <span class="percentage">78%</span>
      <span class="label">Match!</span>
    </div>
  </div>
  
  <div class="compatibility-details">
    <div class="strength">
      <span class="icon">✓</span>
      <span>Both value deep curiosity</span>
    </div>
    <div class="strength">
      <span class="icon">✓</span>
      <span>Similar communication styles</span>
    </div>
    <div class="difference">
      <span class="icon">⚠</span>
      <span>Different energy levels</span>
    </div>
  </div>
</div>
```

**Styles:**
```scss
.compatibility-card {
  background: var(--card-bg);
  border-radius: 16px;
  padding: 24px;
  margin-top: 16px;
  border: 2px solid var(--border);
  
  h3 {
    font-size: 16px;
    text-align: center;
    margin-bottom: 20px;
    color: var(--text-secondary);
  }
  
  .compatibility-score {
    display: flex;
    justify-content: center;
    margin-bottom: 24px;
    
    .score-circle {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      width: 120px;
      height: 120px;
      background: linear-gradient(135deg, var(--primary), var(--secondary));
      border-radius: 50%;
      color: white;
      
      .percentage {
        font-size: 36px;
        font-weight: 700;
      }
      
      .label {
        font-size: 14px;
        margin-top: 4px;
      }
    }
  }
  
  .compatibility-details {
    .strength, .difference {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 12px 0;
      font-size: 14px;
      line-height: 1.5;
      
      .icon {
        flex-shrink: 0;
        width: 20px;
      }
    }
    
    .strength .icon {
      color: var(--success);
    }
    
    .difference .icon {
      color: var(--warning);
    }
  }
}
```

---

## Dashboard Design (NGL-Style)

```html
<div class="dashboard-container">
  <div class="dashboard-header">
    <h1>5 people played with you!</h1>
  </div>
  
  <div class="match-list">
    <div class="match-card" *ngFor="let match of matches">
      <div class="match-emoji">
        {{ match.score >= 80 ? '🔥' : match.score >= 60 ? '💫' : '❤️' }}
      </div>
      <div class="match-info">
        <div class="match-label">Anonymous User</div>
        <div class="match-score">{{ match.score }}% Match</div>
      </div>
      <div class="match-time">{{ match.timeAgo }}</div>
    </div>
  </div>
  
  <div class="share-section">
    <h3>Share your link</h3>
    <div class="share-link">
      <input readonly [value]="shareLink" />
      <button (click)="copyLink()">Copy</button>
    </div>
  </div>
</div>
```

**Styles:**
```scss
.dashboard-container {
  max-width: 500px;
  margin: 0 auto;
  padding: 24px;
  
  .dashboard-header {
    text-align: center;
    margin-bottom: 24px;
    
    h1 {
      font-size: 24px;
      font-weight: 700;
    }
  }
  
  .match-list {
    display: flex;
    flex-direction: column;
    gap: 12px;
    margin-bottom: 32px;
    
    .match-card {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 16px;
      background: var(--card-bg);
      border-radius: 12px;
      border: 1px solid var(--border);
      cursor: pointer;
      transition: transform 0.2s;
      
      &:hover {
        transform: translateX(4px);
      }
      
      .match-emoji {
        font-size: 32px;
      }
      
      .match-info {
        flex: 1;
        
        .match-label {
          font-size: 15px;
          font-weight: 500;
          margin-bottom: 4px;
        }
        
        .match-score {
          font-size: 13px;
          color: var(--text-secondary);
        }
      }
      
      .match-time {
        font-size: 12px;
        color: var(--text-tertiary);
      }
    }
  }
  
  .share-section {
    padding: 20px;
    background: var(--bg);
    border-radius: 12px;
    
    h3 {
      font-size: 16px;
      margin-bottom: 12px;
    }
    
    .share-link {
      display: flex;
      gap: 8px;
      
      input {
        flex: 1;
        padding: 12px;
        border: 1px solid var(--border);
        border-radius: 8px;
        font-size: 14px;
        background: var(--card-bg);
      }
      
      button {
        padding: 12px 24px;
        background: var(--primary);
        color: white;
        border: none;
        border-radius: 8px;
        font-weight: 600;
        cursor: pointer;
        
        &:hover {
          opacity: 0.9;
        }
      }
    }
  }
}
```

---

## Mobile Responsiveness

**Breakpoints:**
```scss
// Already in Woven design system
$mobile: 480px;
$tablet: 768px;
$desktop: 1024px;

@media (max-width: $mobile) {
  .game-card {
    padding: 20px;
    border-radius: 12px;
  }
  
  .selection-grid {
    grid-template-columns: repeat(2, 1fr);
  }
  
  .pillar-bar {
    grid-template-columns: 100px 1fr 45px;
    font-size: 13px;
  }
}
```

---

## Character Limits

**All text inputs:**
```html
<input 
  type="text"
  maxlength="280"
  [(ngModel)]="text"
/>
<div class="char-count" [class.over-limit]="text.length > 280">
  {{ text.length }} / 280
</div>
```

**Limits:**
- Email: 100 chars
- Short answer: 150 chars
- Medium answer: 280 chars
- Long answer: 500 chars

---

## Loading States

```html
<button [disabled]="loading" class="primary-btn">
  <span *ngIf="!loading">Next</span>
  <span *ngIf="loading" class="spinner"></span>
</button>
```

**Spinner:**
```scss
.spinner {
  display: inline-block;
  width: 16px;
  height: 16px;
  border: 2px solid rgba(255,255,255,0.3);
  border-top-color: white;
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}
```

---

## Summary

**Design locked:**
- Reuse existing Woven design tokens
- Clean card-based layouts
- Simple transitions only
- NGL-inspired dashboard
- Mobile-first responsive
- Character limits enforced
- Loading states on all actions

**Next step:** Start building with this spec.
