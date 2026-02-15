# Frontend Patterns Reference

> Quick reference for common frontend patterns in Woven.
> Use these patterns consistently across all frontend code.

---

## 1. Component Communication

### Parent to Child (Input)

```typescript
// Child component
@Component({
  selector: 'app-card',
  template: `<div class="card">{{ title }}</div>`
})
export class CardComponent {
  @Input() title = '';
  @Input() data: CardData | null = null;
}

// Parent template
<app-card [title]="item.name" [data]="item"></app-card>
```

### Child to Parent (Output)

```typescript
// Child component
@Component({
  selector: 'app-card',
  template: `
    <div class="card" (click)="onClick()">
      {{ title }}
    </div>
  `
})
export class CardComponent {
  @Input() title = '';
  @Output() selected = new EventEmitter<void>();
  @Output() action = new EventEmitter<string>();

  onClick() {
    this.selected.emit();
  }

  doAction(type: string) {
    this.action.emit(type);
  }
}

// Parent template
<app-card
  [title]="item.name"
  (selected)="onCardSelected(item)"
  (action)="onCardAction($event, item)">
</app-card>
```

### Two-way Binding

```typescript
// Child component
@Component({
  selector: 'app-toggle',
  template: `
    <button (click)="toggle()">{{ value ? 'ON' : 'OFF' }}</button>
  `
})
export class ToggleComponent {
  @Input() value = false;
  @Output() valueChange = new EventEmitter<boolean>();

  toggle() {
    this.value = !this.value;
    this.valueChange.emit(this.value);
  }
}

// Parent template - two-way binding with [(value)]
<app-toggle [(value)]="isEnabled"></app-toggle>
```

---

## 2. Async Data Loading

### Standard Pattern

```typescript
@Component({...})
export class FeatureComponent implements OnInit {
  loading = true;
  error = '';
  data: FeatureData | null = null;

  constructor(private api: FeatureService) {}

  async ngOnInit() {
    await this.load();
  }

  async load() {
    this.loading = true;
    this.error = '';

    try {
      this.data = await firstValueFrom(this.api.get());
    } catch {
      this.error = 'Could not load data.';
    } finally {
      this.loading = false;
    }
  }
}
```

### Template Pattern

```html
<!-- Loading -->
<div *ngIf="loading" class="state">Loading...</div>

<!-- Error -->
<div *ngIf="error" class="state err">
  {{ error }}
  <button (click)="load()">Retry</button>
</div>

<!-- Success -->
<div *ngIf="!loading && !error && data">
  {{ data.content }}
</div>

<!-- Empty -->
<div *ngIf="!loading && !error && !data" class="empty">
  No data found
</div>
```

---

## 3. Form Handling

### Template-driven Forms

```typescript
@Component({
  selector: 'app-form',
  template: `
    <form (ngSubmit)="submit()">
      <input
        [(ngModel)]="form.name"
        name="name"
        required
        #nameInput="ngModel"
      />
      <div *ngIf="nameInput.invalid && nameInput.touched" class="error">
        Name is required
      </div>

      <button type="submit" [disabled]="submitting">
        {{ submitting ? 'Saving...' : 'Save' }}
      </button>
    </form>
  `,
  imports: [CommonModule, FormsModule]
})
export class FormComponent {
  form = {
    name: '',
    email: ''
  };

  submitting = false;

  async submit() {
    this.submitting = true;
    try {
      await firstValueFrom(this.api.save(this.form));
      // Success handling
    } catch {
      // Error handling
    } finally {
      this.submitting = false;
    }
  }
}
```

### Reactive Forms

```typescript
@Component({
  selector: 'app-form',
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()">
      <input formControlName="name" />
      <div *ngIf="form.get('name')?.errors?.['required']" class="error">
        Name is required
      </div>

      <button type="submit" [disabled]="form.invalid || submitting">
        Save
      </button>
    </form>
  `,
  imports: [CommonModule, ReactiveFormsModule]
})
export class FormComponent implements OnInit {
  form!: FormGroup;
  submitting = false;

  constructor(private fb: FormBuilder, private api: FeatureService) {}

  ngOnInit() {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(50)]],
      email: ['', [Validators.required, Validators.email]]
    });
  }

  async submit() {
    if (this.form.invalid) return;

    this.submitting = true;
    try {
      await firstValueFrom(this.api.save(this.form.value));
    } finally {
      this.submitting = false;
    }
  }
}
```

---

## 4. Subscription Management

### Single Subscription

```typescript
@Component({...})
export class FeatureComponent implements OnInit, OnDestroy {
  private sub?: Subscription;

  ngOnInit() {
    this.sub = this.service.data$.subscribe(data => {
      this.data = data;
    });
  }

  ngOnDestroy() {
    this.sub?.unsubscribe();
  }
}
```

### Multiple Subscriptions

```typescript
@Component({...})
export class FeatureComponent implements OnInit, OnDestroy {
  private subs = new Subscription();

  ngOnInit() {
    this.subs.add(
      this.service.data$.subscribe(data => this.data = data)
    );

    this.subs.add(
      this.service.status$.subscribe(status => this.status = status)
    );
  }

  ngOnDestroy() {
    this.subs.unsubscribe();
  }
}
```

### takeUntil Pattern

```typescript
@Component({...})
export class FeatureComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  ngOnInit() {
    this.service.data$.pipe(
      takeUntil(this.destroy$)
    ).subscribe(data => {
      this.data = data;
    });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
```

### Async Pipe (Best for simple cases)

```html
<!-- No manual subscription needed -->
<div *ngFor="let item of items$ | async">
  {{ item.name }}
</div>

<div *ngIf="loading$ | async" class="spinner">
  Loading...
</div>
```

---

## 5. Navigation

### Programmatic Navigation

```typescript
constructor(private router: Router) {}

// Navigate by URL
goHome() {
  this.router.navigateByUrl('/home');
}

// Navigate with params
goToDetail(id: string) {
  this.router.navigateByUrl(`/items/${id}`);
}

// Navigate with query params
search(query: string) {
  this.router.navigate(['/search'], {
    queryParams: { q: query, page: 1 }
  });
}

// Navigate back
goBack() {
  window.history.back();
}
```

### Reading Route Params

```typescript
constructor(private route: ActivatedRoute) {}

ngOnInit() {
  // Snapshot (one-time read)
  const id = this.route.snapshot.paramMap.get('id');

  // Observable (for when params change)
  this.route.paramMap.pipe(
    takeUntil(this.destroy$)
  ).subscribe(params => {
    const id = params.get('id');
    this.loadItem(id);
  });

  // Query params
  this.route.queryParamMap.pipe(
    takeUntil(this.destroy$)
  ).subscribe(params => {
    this.page = +(params.get('page') ?? 1);
  });
}
```

---

## 6. Conditional Styling

### Class Binding

```html
<!-- Single class -->
<div [class.active]="isActive">Content</div>

<!-- Multiple classes -->
<div [ngClass]="{
  'active': isActive,
  'disabled': isDisabled,
  'error': hasError
}">Content</div>

<!-- Array of classes -->
<div [ngClass]="['base', isActive ? 'active' : 'inactive']">
  Content
</div>
```

### Style Binding

```html
<!-- Single style -->
<div [style.color]="textColor">Content</div>

<!-- With units -->
<div [style.width.px]="width">Content</div>

<!-- Multiple styles -->
<div [ngStyle]="{
  'color': textColor,
  'font-size.px': fontSize,
  'background': isActive ? '#fff' : '#f0f0f0'
}">Content</div>
```

---

## 7. Event Handling

### Click Events

```html
<!-- Basic click -->
<button (click)="handleClick()">Click me</button>

<!-- With event -->
<button (click)="handleClick($event)">Click me</button>

<!-- Stop propagation -->
<button (click)="handleClick($event); $event.stopPropagation()">
  Click me
</button>

<!-- Prevent default -->
<a href="#" (click)="handleClick($event); $event.preventDefault()">
  Click me
</a>
```

### Keyboard Events

```html
<!-- Key events -->
<input (keyup)="onKeyUp($event)" />
<input (keyup.enter)="onEnter()" />
<input (keyup.escape)="onEscape()" />
<input (keydown.control.s)="onSave($event)" />
```

### Form Events

```html
<input
  (input)="onInput($event)"
  (change)="onChange($event)"
  (focus)="onFocus()"
  (blur)="onBlur()"
/>

<form (submit)="onSubmit($event)">
  ...
</form>
```

---

## 8. Touch Targets (iOS Compliance)

### Minimum Sizes

```scss
// All interactive elements must have at least 44px touch target

// Buttons
.button {
  padding: 12px 16px;
  min-height: 44px;
}

// Icon buttons
.icon-button {
  width: 44px;
  height: 44px;
  display: flex;
  align-items: center;
  justify-content: center;
}

// Links in lists
.list-item {
  padding: 12px;
  min-height: 44px;
}

// Input fields
input, textarea {
  padding: 12px 14px;
  min-height: 44px;
}
```

### Spacing Between Targets

```scss
// Adequate spacing to prevent mis-taps
.button-group {
  display: flex;
  gap: 8px;  // Minimum 8px between touch targets
}
```

---

## 9. Loading States

### Button Loading

```html
<button
  [disabled]="loading"
  (click)="submit()"
  class="btn"
>
  <span *ngIf="!loading">Submit</span>
  <span *ngIf="loading">Saving...</span>
</button>
```

### Skeleton Loading

```html
<div *ngIf="loading" class="skeleton">
  <div class="skeleton-line"></div>
  <div class="skeleton-line short"></div>
</div>

<div *ngIf="!loading">
  {{ data.content }}
</div>
```

```scss
.skeleton-line {
  height: 16px;
  background: linear-gradient(90deg, #f0f0f0 25%, #e0e0e0 50%, #f0f0f0 75%);
  background-size: 200% 100%;
  animation: shimmer 1.5s infinite;
  border-radius: 4px;
  margin-bottom: 8px;
}

.skeleton-line.short {
  width: 60%;
}

@keyframes shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}
```

---

## 10. Animation Patterns

### CSS Transitions

```scss
.card {
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.card:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
}
```

### CSS Animations

```scss
@keyframes fadeIn {
  from { opacity: 0; transform: translateY(10px); }
  to { opacity: 1; transform: translateY(0); }
}

.item {
  animation: fadeIn 0.3s ease forwards;
}

// Staggered animation
.item:nth-child(1) { animation-delay: 0.05s; }
.item:nth-child(2) { animation-delay: 0.1s; }
.item:nth-child(3) { animation-delay: 0.15s; }
```

### Angular Animations

```typescript
import { trigger, transition, style, animate } from '@angular/animations';

@Component({
  animations: [
    trigger('fadeSlide', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(10px)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'translateY(0)' }))
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ opacity: 0, transform: 'translateY(-10px)' }))
      ])
    ])
  ]
})
export class Component {
  items = [];
}
```

```html
<div *ngFor="let item of items" @fadeSlide>
  {{ item.name }}
</div>
```

---

## Quick Reference

| Pattern | When to Use |
|---------|-------------|
| `firstValueFrom` | One-time API call in async function |
| `subscribe` + cleanup | Reactive data that changes |
| `async` pipe | Simple template bindings |
| Template forms | Simple forms with few fields |
| Reactive forms | Complex validation, dynamic fields |
| `[class.x]` | Single conditional class |
| `[ngClass]` | Multiple conditional classes |
| `@Input/@Output` | Parent-child communication |
| Service state | Shared state across components |
