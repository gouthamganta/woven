# Frontend Service Template

> Copy this template when creating new Angular services.
> Services handle API communication and state management.

---

## File Location

```
frontend/woven-frontend/src/app/
└── services/
    └── {feature}.service.ts
```

---

## Basic API Service Template

```typescript
// services/{feature}.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

// ═══════════════════════════════════════════════════════════════
// Types
// ═══════════════════════════════════════════════════════════════

export interface {Feature}Item {
  id: string;
  name: string;
  description: string;
  createdAt: string;
}

export interface {Feature}ListResponse {
  items: {Feature}Item[];
  total: number;
}

export interface Create{Feature}Request {
  name: string;
  description: string;
}

export interface Update{Feature}Request {
  name: string;
  description: string;
}

// ═══════════════════════════════════════════════════════════════
// Service
// ═══════════════════════════════════════════════════════════════

@Injectable({ providedIn: 'root' })
export class {Feature}Service {
  private readonly base = `${environment.apiUrl}/api/{feature}`;

  constructor(private http: HttpClient) {}

  // ─────────────────────────────────────────────────────────────
  // Read operations
  // ─────────────────────────────────────────────────────────────

  /**
   * Get all items for the current user
   */
  list(): Observable<{Feature}ListResponse> {
    return this.http.get<{Feature}ListResponse>(this.base);
  }

  /**
   * Get a single item by ID
   */
  get(id: string): Observable<{Feature}Item> {
    return this.http.get<{Feature}Item>(`${this.base}/${id}`);
  }

  // ─────────────────────────────────────────────────────────────
  // Write operations
  // ─────────────────────────────────────────────────────────────

  /**
   * Create a new item
   */
  create(data: Create{Feature}Request): Observable<{Feature}Item> {
    return this.http.post<{Feature}Item>(this.base, data);
  }

  /**
   * Update an existing item
   */
  update(id: string, data: Update{Feature}Request): Observable<{Feature}Item> {
    return this.http.put<{Feature}Item>(`${this.base}/${id}`, data);
  }

  /**
   * Delete an item
   */
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  // ─────────────────────────────────────────────────────────────
  // Action operations
  // ─────────────────────────────────────────────────────────────

  /**
   * Perform a specific action on an item
   */
  performAction(id: string): Observable<{ success: boolean }> {
    return this.http.post<{ success: boolean }>(`${this.base}/${id}/action`, {});
  }
}
```

---

## Service with State Management

For services that maintain local state:

```typescript
// services/{feature}.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface {Feature}State {
  items: {Feature}Item[];
  loading: boolean;
  error: string;
  lastUpdated: Date | null;
}

@Injectable({ providedIn: 'root' })
export class {Feature}Service {
  private readonly base = `${environment.apiUrl}/api/{feature}`;

  // ─────────────────────────────────────────────────────────────
  // State
  // ─────────────────────────────────────────────────────────────

  private readonly _state$ = new BehaviorSubject<{Feature}State>({
    items: [],
    loading: false,
    error: '',
    lastUpdated: null,
  });

  // Public observables
  readonly state$ = this._state$.asObservable();
  readonly items$ = this.state$.pipe(map(s => s.items));
  readonly loading$ = this.state$.pipe(map(s => s.loading));
  readonly error$ = this.state$.pipe(map(s => s.error));

  constructor(private http: HttpClient) {}

  // ─────────────────────────────────────────────────────────────
  // State helpers
  // ─────────────────────────────────────────────────────────────

  private updateState(partial: Partial<{Feature}State>) {
    this._state$.next({ ...this._state$.value, ...partial });
  }

  private get state(): {Feature}State {
    return this._state$.value;
  }

  // ─────────────────────────────────────────────────────────────
  // Operations
  // ─────────────────────────────────────────────────────────────

  /**
   * Load items and update local state
   */
  load(): Observable<{Feature}ListResponse> {
    this.updateState({ loading: true, error: '' });

    return this.http.get<{Feature}ListResponse>(this.base).pipe(
      tap({
        next: (res) => {
          this.updateState({
            items: res.items,
            loading: false,
            lastUpdated: new Date(),
          });
        },
        error: () => {
          this.updateState({
            loading: false,
            error: 'Failed to load items',
          });
        },
      })
    );
  }

  /**
   * Add item and update local state
   */
  add(data: Create{Feature}Request): Observable<{Feature}Item> {
    return this.http.post<{Feature}Item>(this.base, data).pipe(
      tap((item) => {
        this.updateState({
          items: [...this.state.items, item],
        });
      })
    );
  }

  /**
   * Remove item from local state
   */
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`).pipe(
      tap(() => {
        this.updateState({
          items: this.state.items.filter(i => i.id !== id),
        });
      })
    );
  }

  /**
   * Clear local state
   */
  clear() {
    this._state$.next({
      items: [],
      loading: false,
      error: '',
      lastUpdated: null,
    });
  }
}
```

---

## Service with Query Parameters

```typescript
// services/search.service.ts

export interface SearchParams {
  query?: string;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDir?: 'asc' | 'desc';
  filters?: Record<string, string>;
}

@Injectable({ providedIn: 'root' })
export class SearchService {
  private readonly base = `${environment.apiUrl}/api/search`;

  constructor(private http: HttpClient) {}

  search(params: SearchParams): Observable<SearchResponse> {
    // Build query params
    let httpParams = new HttpParams();

    if (params.query) {
      httpParams = httpParams.set('q', params.query);
    }

    if (params.page) {
      httpParams = httpParams.set('page', params.page.toString());
    }

    if (params.pageSize) {
      httpParams = httpParams.set('pageSize', params.pageSize.toString());
    }

    if (params.sortBy) {
      httpParams = httpParams.set('sortBy', params.sortBy);
      httpParams = httpParams.set('sortDir', params.sortDir || 'asc');
    }

    if (params.filters) {
      for (const [key, value] of Object.entries(params.filters)) {
        httpParams = httpParams.set(`filter[${key}]`, value);
      }
    }

    return this.http.get<SearchResponse>(this.base, { params: httpParams });
  }
}
```

---

## Service with File Upload

```typescript
// services/photo.service.ts

export interface UploadResult {
  id: string;
  url: string;
}

@Injectable({ providedIn: 'root' })
export class PhotoService {
  private readonly base = `${environment.apiUrl}/api/photos`;

  constructor(private http: HttpClient) {}

  /**
   * Upload a photo file
   */
  upload(file: File): Observable<UploadResult> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<UploadResult>(`${this.base}/upload`, formData);
  }

  /**
   * Upload with progress tracking
   */
  uploadWithProgress(file: File): Observable<HttpEvent<UploadResult>> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<UploadResult>(`${this.base}/upload`, formData, {
      reportProgress: true,
      observe: 'events',
    });
  }

  /**
   * Delete a photo
   */
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
```

---

## Service with Caching

```typescript
// services/config.service.ts

@Injectable({ providedIn: 'root' })
export class ConfigService {
  private readonly base = `${environment.apiUrl}/api/config`;
  private cache: AppConfig | null = null;
  private cacheTime: number = 0;
  private readonly CACHE_DURATION = 5 * 60 * 1000; // 5 minutes

  constructor(private http: HttpClient) {}

  /**
   * Get config with caching
   */
  getConfig(): Observable<AppConfig> {
    const now = Date.now();

    // Return cached if valid
    if (this.cache && now - this.cacheTime < this.CACHE_DURATION) {
      return of(this.cache);
    }

    // Fetch fresh
    return this.http.get<AppConfig>(this.base).pipe(
      tap((config) => {
        this.cache = config;
        this.cacheTime = now;
      })
    );
  }

  /**
   * Force refresh cache
   */
  refresh(): Observable<AppConfig> {
    this.cache = null;
    this.cacheTime = 0;
    return this.getConfig();
  }
}
```

---

## Usage in Components

### One-time fetch (most common)

```typescript
import { firstValueFrom } from 'rxjs';

async ngOnInit() {
  try {
    const data = await firstValueFrom(this.featureService.list());
    this.items = data.items;
  } catch {
    this.error = 'Failed to load';
  }
}
```

### Reactive subscription (for real-time updates)

```typescript
private subs = new Subscription();

ngOnInit() {
  this.subs.add(
    this.featureService.items$.subscribe(items => {
      this.items = items;
    })
  );

  // Trigger load
  this.featureService.load().subscribe();
}

ngOnDestroy() {
  this.subs.unsubscribe();
}
```

### With async pipe (in template)

```html
<div *ngFor="let item of featureService.items$ | async">
  {{ item.name }}
</div>
```

---

## Error Handling

### In service (for state management)

```typescript
load(): Observable<Response> {
  return this.http.get<Response>(this.base).pipe(
    catchError((err) => {
      this.updateState({ error: 'Failed to load' });
      return throwError(() => err);
    })
  );
}
```

### In component (recommended)

```typescript
async load() {
  this.loading = true;
  this.error = '';

  try {
    const data = await firstValueFrom(this.api.list());
    this.items = data.items;
  } catch (err: any) {
    // Map error to user-friendly message
    this.error = this.mapError(err);
  } finally {
    this.loading = false;
  }
}

private mapError(err: any): string {
  const code = err?.error?.error;
  switch (code) {
    case 'NOT_FOUND': return 'Item not found';
    case 'FORBIDDEN': return 'You don\'t have access';
    default: return 'Something went wrong';
  }
}
```

---

## Checklist

Before committing a new service:

- [ ] Uses `@Injectable({ providedIn: 'root' })`
- [ ] Types defined for all request/response shapes
- [ ] Methods return `Observable<T>`
- [ ] Base URL uses `environment.apiUrl`
- [ ] Methods have JSDoc comments
- [ ] Error handling strategy is clear
- [ ] Caching implemented if needed
- [ ] State management is clean (if applicable)
