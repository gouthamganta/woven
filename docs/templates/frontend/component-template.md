# Frontend Component Template

> Copy this template when creating new Angular components.
> Always use standalone components - never NgModules.

---

## File Structure

```
frontend/woven-frontend/src/app/
└── pages/
    └── {feature}/
        ├── {feature}.page.ts      ← Component class
        ├── {feature}.page.html    ← Template
        └── {feature}.page.scss    ← Styles
```

Or for shared components:
```
frontend/woven-frontend/src/app/
└── components/
    └── {component}/
        └── {component}.component.ts  ← All-in-one (inline template/styles)
```

---

## Page Component Template

### TypeScript File

```typescript
// pages/{feature}/{feature}.page.ts

import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom, Subscription } from 'rxjs';
import { {Feature}Service, {Feature}Response } from '../../services/{feature}.service';

@Component({
  selector: 'app-{feature}-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './{feature}.page.html',
  styleUrls: ['./{feature}.page.scss'],
})
export class {Feature}PageComponent implements OnInit, OnDestroy {
  // ═══════════════════════════════════════════════════════════════
  // State
  // ═══════════════════════════════════════════════════════════════

  loading = true;
  error = '';
  data: {Feature}Response | null = null;

  // For subscriptions that need cleanup
  private subs = new Subscription();

  // ═══════════════════════════════════════════════════════════════
  // Constructor
  // ═══════════════════════════════════════════════════════════════

  constructor(
    private api: {Feature}Service,
    private router: Router
  ) {}

  // ═══════════════════════════════════════════════════════════════
  // Lifecycle
  // ═══════════════════════════════════════════════════════════════

  async ngOnInit() {
    await this.load();
  }

  ngOnDestroy() {
    this.subs.unsubscribe();
  }

  // ═══════════════════════════════════════════════════════════════
  // Data Loading
  // ═══════════════════════════════════════════════════════════════

  async load() {
    this.loading = true;
    this.error = '';

    try {
      this.data = await firstValueFrom(this.api.getData());
    } catch {
      this.error = 'Could not load data. Please try again.';
    } finally {
      this.loading = false;
    }
  }

  // ═══════════════════════════════════════════════════════════════
  // Actions
  // ═══════════════════════════════════════════════════════════════

  async performAction() {
    this.loading = true;
    this.error = '';

    try {
      await firstValueFrom(this.api.doAction());
      // Handle success (navigate, refresh, etc.)
    } catch {
      this.error = 'Action failed. Please try again.';
    } finally {
      this.loading = false;
    }
  }

  // ═══════════════════════════════════════════════════════════════
  // Navigation
  // ═══════════════════════════════════════════════════════════════

  goBack() {
    this.router.navigateByUrl('/home');
  }

  // ═══════════════════════════════════════════════════════════════
  // Helpers (for template)
  // ═══════════════════════════════════════════════════════════════

  get isEmpty(): boolean {
    return !this.data || this.data.items.length === 0;
  }
}
```

### HTML Template

```html
<!-- pages/{feature}/{feature}.page.html -->

<div class="page">
  <!-- Top bar -->
  <div class="top">
    <div class="brand">WOVEN</div>
    <div class="meta">{Feature}</div>
  </div>

  <!-- Background watermark -->
  <div class="watermark">W</div>

  <!-- Main content -->
  <div class="content">

    <!-- Header section -->
    <div class="head">
      <div>
        <div class="kicker">YOUR {FEATURE}</div>
        <h1 class="title">Page Title</h1>
        <p class="sub">Brief description of what this page shows.</p>
      </div>

      <div class="right">
        <button class="pill" (click)="performAction()">
          Action
        </button>
      </div>
    </div>

    <!-- Loading state -->
    <div *ngIf="loading" class="state">
      Loading...
    </div>

    <!-- Error state -->
    <div *ngIf="error" class="state err">
      {{ error }}
      <button class="pill" (click)="load()">Try Again</button>
    </div>

    <!-- Empty state -->
    <div *ngIf="!loading && !error && isEmpty" class="empty">
      <div class="emptyTitle">Nothing here yet</div>
      <div class="emptySub">Description of empty state.</div>
    </div>

    <!-- Data list -->
    <div *ngIf="!loading && !error && !isEmpty" class="list">
      <div class="row" *ngFor="let item of data?.items" (click)="select(item)">
        <div class="left">
          <div class="photo">
            <img [src]="item.photo" [alt]="item.name" />
          </div>
          <div class="info">
            <div class="name">{{ item.name }}</div>
            <div class="mini">{{ item.description }}</div>
          </div>
        </div>
        <div class="rightSide">
          <div class="openPill">Open</div>
        </div>
      </div>
    </div>

  </div>
</div>
```

### SCSS Styles

```scss
// pages/{feature}/{feature}.page.scss

// ═══════════════════════════════════════════════════════════════
// Layout
// ═══════════════════════════════════════════════════════════════

.page {
  min-height: 100vh;
  position: relative;
  overflow: hidden;
  padding: 86px 16px 100px;
}

.top {
  position: fixed;
  top: 20px;
  left: 22px;
  right: 22px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  opacity: 0.92;
  z-index: 5;
  pointer-events: none;
}

.brand {
  letter-spacing: 0.22em;
  font-weight: 750;
  font-size: 12px;
  text-transform: uppercase;
}

.meta {
  font-size: 12px;
  opacity: 0.65;
}

.watermark {
  position: absolute;
  right: -40px;
  top: -30px;
  font-size: 240px;
  font-weight: 800;
  letter-spacing: -0.06em;
  color: rgba(0, 0, 0, 0.035);
  transform: rotate(-8deg);
  user-select: none;
  pointer-events: none;
  z-index: 0;
}

.content {
  max-width: 760px;
  margin: 0 auto;
  position: relative;
  z-index: 2;
  display: grid;
  gap: 14px;
}

// ═══════════════════════════════════════════════════════════════
// Header
// ═══════════════════════════════════════════════════════════════

.head {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: flex-start;
}

.kicker {
  font-size: 11px;
  font-weight: 900;
  letter-spacing: 0.28em;
  text-transform: uppercase;
  opacity: 0.7;
}

.title {
  font-size: 20px;
  font-weight: 900;
  letter-spacing: -0.03em;
  margin-top: 4px;
}

.sub {
  font-size: 13px;
  opacity: 0.68;
  margin-top: 4px;
  max-width: 52ch;
}

.right {
  display: flex;
  gap: 8px;
  align-items: center;
}

// ═══════════════════════════════════════════════════════════════
// Interactive Elements (44px touch targets)
// ═══════════════════════════════════════════════════════════════

.pill {
  border: 1px solid rgba(0, 0, 0, 0.12);
  background: rgba(255, 255, 255, 0.82);
  padding: 12px 16px;
  min-height: 44px;
  border-radius: 999px;
  font-size: 13px;
  font-weight: 850;
  cursor: pointer;
  transition: all 0.2s ease;
}

.pill:hover {
  background: rgba(255, 255, 255, 1);
  border-color: rgba(0, 0, 0, 0.18);
}

.pill.outline {
  background: transparent;
}

.pill.outline:hover {
  background: rgba(255, 255, 255, 0.5);
}

// ═══════════════════════════════════════════════════════════════
// States (Loading, Error, Empty)
// ═══════════════════════════════════════════════════════════════

.state {
  padding: 14px;
  opacity: 0.7;
  text-align: center;
}

.state.err {
  opacity: 1;
  color: #c00;
}

.empty {
  background: rgba(255, 255, 255, 0.82);
  border: 1px solid rgba(0, 0, 0, 0.08);
  border-radius: 18px;
  padding: 16px;
  text-align: center;
}

.emptyTitle {
  font-weight: 900;
  font-size: 16px;
  margin-bottom: 6px;
}

.emptySub {
  font-size: 13px;
  opacity: 0.7;
}

// ═══════════════════════════════════════════════════════════════
// List & Rows
// ═══════════════════════════════════════════════════════════════

.list {
  display: grid;
  gap: 10px;
}

.row {
  width: 100%;
  text-align: left;
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid rgba(0, 0, 0, 0.08);
  border-radius: 20px;
  padding: 12px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  cursor: pointer;
  transition: transform 0.12s ease, box-shadow 0.12s ease;
}

.row:hover {
  transform: translateY(-1px);
  box-shadow: 0 16px 60px rgba(0, 0, 0, 0.1);
}

.left {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  min-width: 0;
}

.photo {
  width: 62px;
  height: 62px;
  border-radius: 18px;
  overflow: hidden;
  border: 1px solid rgba(0, 0, 0, 0.08);
  background: rgba(0, 0, 0, 0.03);
  flex: 0 0 auto;
}

.photo img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.info {
  min-width: 0;
  flex: 1;
}

.name {
  font-size: 15px;
  font-weight: 950;
  letter-spacing: -0.01em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.mini {
  margin-top: 4px;
  font-size: 12px;
  opacity: 0.65;
  line-height: 1.3;
}

.rightSide {
  display: flex;
  align-items: center;
}

.openPill {
  border: 1px solid rgba(0, 0, 0, 0.12);
  background: rgba(0, 0, 0, 0.04);
  padding: 10px 16px;
  min-height: 44px;
  border-radius: 999px;
  font-size: 13px;
  font-weight: 900;
  transition: all 0.2s ease;
}

.openPill:hover {
  background: rgba(0, 0, 0, 0.08);
  border-color: rgba(0, 0, 0, 0.18);
}

// ═══════════════════════════════════════════════════════════════
// Mobile
// ═══════════════════════════════════════════════════════════════

@media (max-width: 520px) {
  .row {
    align-items: flex-start;
  }

  .head {
    flex-direction: column;
    gap: 16px;
  }

  .right {
    width: 100%;
  }

  .pill {
    flex: 1;
    text-align: center;
  }
}
```

---

## Inline Component Template (for smaller components)

```typescript
// components/{component}/{component}.component.ts

import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-{component}',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="wrapper" [class.active]="active">
      <div class="label">{{ label }}</div>
      <button class="btn" (click)="handleClick()">
        {{ buttonText }}
      </button>
    </div>
  `,
  styles: [`
    .wrapper {
      display: flex;
      gap: 12px;
      align-items: center;
      padding: 12px;
      border-radius: 12px;
      background: rgba(255,255,255,0.9);
      border: 1px solid rgba(0,0,0,0.08);
    }
    .wrapper.active {
      border-color: rgba(0,0,0,0.2);
    }
    .label {
      flex: 1;
      font-weight: 600;
    }
    .btn {
      padding: 12px 16px;
      min-height: 44px;
      border-radius: 999px;
      border: none;
      background: #111;
      color: #fff;
      font-weight: 700;
      cursor: pointer;
    }
  `]
})
export class {Component}Component {
  @Input() label = '';
  @Input() buttonText = 'Action';
  @Input() active = false;

  @Output() action = new EventEmitter<void>();

  handleClick() {
    this.action.emit();
  }
}
```

---

## Route Registration

```typescript
// app.routes.ts

import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'feature',
    loadComponent: () =>
      import('./pages/feature/feature.page').then(m => m.FeaturePageComponent),
  },
  // Lazy load with children
  {
    path: 'home',
    loadComponent: () =>
      import('./pages/home/home').then(m => m.HomeComponent),
    children: [
      {
        path: 'feature',
        loadComponent: () =>
          import('./pages/feature/feature.page').then(m => m.FeaturePageComponent),
      },
    ],
  },
];
```

---

## Checklist

Before committing a new component:

- [ ] Uses `standalone: true`
- [ ] Imports `CommonModule` (for *ngIf, *ngFor, etc.)
- [ ] Has loading state handled
- [ ] Has error state handled
- [ ] Has empty state handled (if applicable)
- [ ] All touch targets ≥ 44px
- [ ] Uses `firstValueFrom` for one-time API calls
- [ ] Cleans up subscriptions in `ngOnDestroy`
- [ ] Route registered in `app.routes.ts`
- [ ] Follows existing design patterns (watermark, top bar, etc.)
