import { ChangeDetectorRef, ElementRef, NgZone } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { LoginComponent } from './login';
import { environment } from '../../../environments/environment';

describe('Login credential and intro contracts without Google network access', () => {
  let page: LoginComponent;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.fn>;
  let initialize: ReturnType<typeof vi.fn>;
  let renderButton: ReturnType<typeof vi.fn>;
  const invoke = (name: string, ...args: unknown[]) => (page as any)[name](...args);

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.fn(); initialize = vi.fn(); renderButton = vi.fn();
    window.google = { accounts: { id: { initialize, renderButton } } };
    page = new LoginComponent(TestBed.inject(HttpClient), { navigateByUrl: navigate } as unknown as Router,
      { run: (fn: () => unknown) => fn() } as NgZone,
      { markForCheck: vi.fn() } as unknown as ChangeDetectorRef, 'browser' as unknown as object);
    document.body.innerHTML = '<div class="intro"></div><div class="card"></div><label class="consent"></label><div id="googleBtn"></div>';
    page.introRef = new ElementRef(document.querySelector<HTMLDivElement>('.intro')!);
    page.cardRef = new ElementRef(document.querySelector<HTMLDivElement>('.card')!);
    localStorage.clear();
  });
  afterEach(() => {
    page.ngOnDestroy(); http.verify(); vi.clearAllTimers(); vi.useRealTimers(); vi.restoreAllMocks();
    document.body.innerHTML = ''; delete window.google; localStorage.clear();
  });

  it('rejects missing credential locally without sending an authentication request', () => {
    invoke('onGoogleCredential', {});
    expect(page.isLoading).toBe(false); expect(page.errorMsg).toContain('token missing');
    http.expectNone(`${environment.apiUrl}/auth/google`); expect(navigate).not.toHaveBeenCalled();
  });
  it('posts the credential, stores the authenticated identity, and replaces login in history', () => {
    invoke('onGoogleCredential', { credential: 'synthetic-id-token' });
    expect(page.isLoading).toBe(true);
    const req = http.expectOne(`${environment.apiUrl}/auth/google`);
    expect(req.request.method).toBe('POST'); expect(req.request.body).toEqual({ idToken: 'synthetic-id-token' });
    req.flush({ accessToken: 'synthetic-access-token', user: { id: 'synthetic-user' } });
    expect(localStorage.getItem('accessToken')).toBe('synthetic-access-token');
    expect(JSON.parse(localStorage.getItem('user')!)).toEqual({ id: 'synthetic-user' });
    expect(navigate).toHaveBeenCalledWith('/app', { replaceUrl: true }); expect(page.isLoading).toBe(false);
  });
  it('clears pending state and displays a retry message when authentication is rejected', () => {
    invoke('onGoogleCredential', { credential: 'synthetic-invalid-token' });
    http.expectOne(`${environment.apiUrl}/auth/google`).flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(page.errorMsg).toContain('Login failed'); expect(page.isLoading).toBe(false);
    expect(localStorage.getItem('accessToken')).toBeNull(); expect(navigate).not.toHaveBeenCalled();
  });
  it('reports an unavailable Google SDK without trying to render a button', () => {
    delete window.google; invoke('renderGoogleButton');
    expect(page.errorMsg).toContain('failed to load'); expect(renderButton).not.toHaveBeenCalled();
  });
  it('initializes the configured client and routes the SDK callback through credential validation', () => {
    invoke('renderGoogleButton');
    expect(initialize.mock.calls[0][0].client_id).toBe(environment.googleClientId);
    expect(renderButton.mock.calls[0][0]).toBe(document.getElementById('googleBtn'));
    initialize.mock.calls[0][0].callback(null); expect(page.errorMsg).toContain('token missing');
  });
  it('does not render into a missing button host', () => {
    document.getElementById('googleBtn')!.remove(); invoke('renderGoogleButton');
    expect(initialize).toHaveBeenCalledOnce(); expect(renderButton).not.toHaveBeenCalled();
  });
  it('toggles consent and removes the locked-click attention animation after 500ms', () => {
    invoke('toggleAgree'); expect(page.agreed).toBe(true); invoke('toggleAgree'); expect(page.agreed).toBe(false);
    invoke('onLockedClick'); expect(document.querySelector('.consent')!.classList.contains('shake')).toBe(true);
    vi.advanceTimersByTime(500); expect(document.querySelector('.consent')!.classList.contains('shake')).toBe(false);
    document.querySelector('.consent')!.remove(); expect(() => invoke('onLockedClick')).not.toThrow();
  });
  it('skips the intro immediately and updates the card spotlight until destruction', () => {
    invoke('skipIntro'); expect(page.introRef!.nativeElement.style.display).toBe('none');
    expect(page.cardRef!.nativeElement.style.opacity).toBe('1');
    document.dispatchEvent(new MouseEvent('mousemove', { clientX: 31, clientY: 47 }));
    expect(page.cardRef!.nativeElement.style.getPropertyValue('--cx')).toBe('31px');
    expect(page.cardRef!.nativeElement.style.getPropertyValue('--cy')).toBe('47px');
    vi.advanceTimersByTime(80); expect(renderButton).toHaveBeenCalledOnce(); page.ngOnDestroy();
    document.dispatchEvent(new MouseEvent('mousemove', { clientX: 99 }));
    expect(page.cardRef!.nativeElement.style.getPropertyValue('--cx')).toBe('31px');
  });
  it('fades the intro when the video ends and renders the sign-in button after the transition', () => {
    invoke('onVideoEnd'); expect(page.introRef!.nativeElement.classList.contains('fade-out')).toBe(true);
    vi.advanceTimersByTime(800); expect(page.cardRef!.nativeElement.classList.contains('visible')).toBe(true);
    expect(renderButton).toHaveBeenCalledOnce();
  });
  it('plays ready video muted from the beginning', () => {
    const play = vi.fn().mockResolvedValue(undefined);
    page.videoRef = new ElementRef({ readyState: 3, play, currentTime: 42, muted: false } as unknown as HTMLVideoElement);
    invoke('startVideo'); expect(play).toHaveBeenCalledOnce(); expect(page.videoRef.nativeElement.muted).toBe(true);
    expect(page.videoRef.nativeElement.currentTime).toBe(0);
  });
  it('waits for canplay and keeps the fallback available when autoplay is rejected', async () => {
    const video = document.createElement('video');
    const play = vi.spyOn(video, 'play').mockRejectedValue(new Error('synthetic autoplay restriction'));
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    page.videoRef = new ElementRef(video); invoke('startVideo'); expect(play).not.toHaveBeenCalled();
    video.dispatchEvent(new Event('canplay')); await Promise.resolve();
    expect(play).toHaveBeenCalledOnce(); expect(warn).toHaveBeenCalledOnce();
    video.dispatchEvent(new Event('canplay')); expect(play).toHaveBeenCalledOnce();
  });
  it('reveals the card if the video reference is absent', () => {
    page.ngAfterViewInit(); vi.advanceTimersByTime(1000); expect(renderButton).toHaveBeenCalledOnce();
  });
  it('uses the 13-second fallback when a video never becomes playable', () => {
    page.videoRef = new ElementRef(document.createElement('video'));
    page.ngAfterViewInit(); vi.advanceTimersByTime(13800); expect(renderButton).toHaveBeenCalledOnce();
  });
  it('avoids intro timers on the server and tolerates missing visual references', () => {
    const server = new LoginComponent(TestBed.inject(HttpClient), {} as Router, {} as NgZone,
      {} as ChangeDetectorRef, 'server' as unknown as object);
    server.ngAfterViewInit(); expect(vi.getTimerCount()).toBe(0); server.ngOnDestroy();
    page.introRef = undefined; page.cardRef = undefined; invoke('showCard', true);
    vi.advanceTimersByTime(80); expect(renderButton).toHaveBeenCalledOnce();
  });
});
