import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { StartOnboardingComponent } from './start';
import { environment } from '../../../environments/environment';

describe('Onboarding preparation polling contracts', () => {
  let page: StartOnboardingComponent;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController); navigate = vi.fn();
    page = new StartOnboardingComponent(TestBed.inject(HttpClient), { navigateByUrl: navigate } as unknown as Router,
      { markForCheck: vi.fn() } as unknown as ChangeDetectorRef, 'browser' as unknown as object);
  });
  afterEach(() => { page.ngOnDestroy(); http.verify(); vi.clearAllTimers(); vi.useRealTimers(); });
  it('does not start browser polling during server rendering', () => {
    const server = new StartOnboardingComponent(TestBed.inject(HttpClient), {} as Router, {} as ChangeDetectorRef,
      'server' as unknown as object);
    server.ngOnInit(); expect(vi.getTimerCount()).toBe(0); server.ngOnDestroy();
  });
  it('waits for COMPLETE before showing ready and stops subsequent polls', async () => {
    page.ngOnInit(); vi.advanceTimersByTime(3000);
    http.expectOne(`${environment.apiUrl}/onboarding/state`).flush({ profileStatus: 'PROCESSING' });
    await Promise.resolve(); expect(page.ready).toBe(false);
    vi.advanceTimersByTime(3000); http.expectOne(`${environment.apiUrl}/onboarding/state`).flush({ profileStatus: 'COMPLETE' });
    await Promise.resolve(); expect(page.ready).toBe(true);
    vi.advanceTimersByTime(9000); http.expectNone(`${environment.apiUrl}/onboarding/state`);
  });
  it('retries after a failed poll without claiming the profile is complete', async () => {
    page.ngOnInit(); vi.advanceTimersByTime(3000);
    http.expectOne(`${environment.apiUrl}/onboarding/state`).flush({}, { status: 503, statusText: 'Unavailable' });
    await Promise.resolve(); expect(page.ready).toBe(false);
    vi.advanceTimersByTime(3000); http.expectOne(`${environment.apiUrl}/onboarding/state`).flush({ profileStatus: 'COMPLETE' });
    await Promise.resolve(); expect(page.ready).toBe(true);
  });
  it('cycles all four preparation messages while waiting', () => {
    (page as any).rotateStatus();
    for (const text of ['Finding compatible matches', 'Reviewing shared interests', 'Preparing your deck', 'Analyzing your profile']) {
      vi.advanceTimersByTime(2500); expect(page.statusText).toBe(text);
    }
  });
  it('records the current 15-second readiness fallback separately from verified completion', async () => {
    page.ngOnInit();
    for (let i = 0; i < 5; i++) {
      vi.advanceTimersByTime(3000); http.expectOne(`${environment.apiUrl}/onboarding/state`).flush({ profileStatus: 'PROCESSING' });
      await Promise.resolve();
    }
    expect(page.ready).toBe(true); // Existing UI fallback; this is not evidence that the backend completed.
  });
  it('stops intervals when destroyed before the next poll', () => {
    page.ngOnInit(); page.ngOnDestroy(); vi.advanceTimersByTime(6000);
    http.expectNone(`${environment.apiUrl}/onboarding/state`);
  });
  it('routes the entry action to Moments', () => { page.enter(); expect(navigate).toHaveBeenCalledWith('/moments'); });
});
