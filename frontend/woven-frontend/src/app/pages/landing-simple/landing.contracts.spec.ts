import { ChangeDetectorRef, ElementRef } from '@angular/core';
import { vi } from 'vitest';
import { LandingSimpleComponent } from './landing-simple.component';
import { gsap } from 'gsap';
import { Draggable } from 'gsap/Draggable';

const motion = vi.hoisted(() => ({ to: vi.fn(), fromTo: vi.fn(), set: vi.fn(), create: vi.fn(), getProperty: vi.fn(), registerPlugin: vi.fn() }));

describe('Landing media and motion contracts', () => {
  let page: LandingSimpleComponent;
  const elements: HTMLElement[] = [];
  beforeEach(() => {
    vi.useFakeTimers(); Object.values(motion).forEach(fn => fn.mockReset()); motion.getProperty.mockReturnValue(-840);
    for (const name of ['to', 'fromTo', 'set', 'getProperty', 'registerPlugin'] as const)
      vi.spyOn(gsap, name).mockImplementation(motion[name] as any);
    vi.spyOn(gsap.utils, 'toArray').mockImplementation(((selector: string) => Array.from(document.querySelectorAll(selector))) as any);
    vi.spyOn(Draggable, 'create').mockImplementation(motion.create as any);
    vi.spyOn(console, 'log').mockImplementation(() => {}); vi.spyOn(console, 'warn').mockImplementation(() => {});
    page = new LandingSimpleComponent('browser' as unknown as object, { markForCheck: vi.fn() } as unknown as ChangeDetectorRef);
  });
  afterEach(() => { page.ngOnDestroy(); vi.clearAllTimers(); vi.useRealTimers(); vi.restoreAllMocks(); elements.splice(0).forEach(el => el.remove()); });
  function add(className: string) { const el = document.createElement('div'); el.className = className; document.body.appendChild(el); elements.push(el); return el; }
  async function flushMotion() { await vi.advanceTimersByTimeAsync(100); await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); }
  function video({ ready = 4, paused = true, failed = false } = {}) { const el = document.createElement('video'); Object.defineProperty(el, 'readyState', { value: ready }); Object.defineProperty(el, 'paused', { value: paused }); el.play = failed ? vi.fn().mockRejectedValue(new Error('Unit autoplay denial')) : vi.fn().mockResolvedValue(undefined); page.videoRef = new ElementRef(el); return el; }
  it('skips browser behavior during server rendering', () => { const server = new LandingSimpleComponent('server' as unknown as object, { markForCheck: vi.fn() } as unknown as ChangeDetectorRef); server.ngAfterViewInit(); expect(server.showIntro).toBe(true); server.ngOnDestroy(); });
  for (const width of [390, 1366]) {
    it(`selects the correct intro and autoplay policy at ${width}px`, async () => {
      vi.spyOn(window, 'innerWidth', 'get').mockReturnValue(width); const el = video(); page.ngAfterViewInit(); expect(page.isMobileDevice).toBe(width <= 768); expect(page.introVideoSrc).toBe(width <= 768 ? '/login-intro-mobile.mp4' : '/login-intro.mp4'); await vi.advanceTimersByTimeAsync(200); expect(el.play).toHaveBeenCalledTimes(width <= 768 ? 0 : 1); page.onVideoClick(); await Promise.resolve(); expect(page.videoPlaying).toBe(true);
    });
  }
  it('waits for playable media and handles autoplay denial', async () => { const el = video({ ready: 0, failed: true }); page.ngAfterViewInit(); await vi.advanceTimersByTimeAsync(200); expect(el.play).not.toHaveBeenCalled(); el.dispatchEvent(new Event('canplay')); await Promise.resolve(); await Promise.resolve(); expect(page.videoPlaying).toBe(false); page.onVideoClick(); await Promise.resolve(); });
  it('does not replay already-playing media and tolerates absent video references', async () => { page.onVideoClick(); const el = video({ paused: false }); page.onVideoClick(); expect(el.play).not.toHaveBeenCalled(); page.videoRef = undefined; page.ngAfterViewInit(); await vi.advanceTimersByTimeAsync(200); expect(page.showIntro).toBe(false); });
  it('hides the intro on timeout or completion and initializes only present motion targets', async () => { page.ngAfterViewInit(); await vi.advanceTimersByTimeAsync(13000); expect(page.showIntro).toBe(false); await flushMotion(); expect(motion.create).not.toHaveBeenCalled(); page.showIntro = true; page.onVideoEnd(); expect(page.showIntro).toBe(false); });
  it('configures bounded carousels, snap behavior and centered-card state after skipping', async () => {
    add('hero-visual'); add('polaroid-carousel'); add('carousel-track'); const cards = [add('polaroid'), add('polaroid'), add('polaroid')]; add('about-track'); add('about-slide'); add('about-slide'); const grid = add('commons-grid'); const item = document.createElement('div'); item.className = 'grid-item'; grid.appendChild(item);
    page.skipIntro(); await flushMotion(); await vi.waitFor(() => expect(motion.create).toHaveBeenCalledTimes(2)); expect(page.showIntro).toBe(false); const options = motion.create.mock.calls[0][1]; expect(options.bounds).toEqual({ minX: -840, maxX: 0 }); expect(options.snap.x(-800)).toBe(-840); options.onDrag(); expect(cards[2].classList.contains('center-card')).toBe(true); expect(cards[0].classList.contains('center-card')).toBe(false); await vi.waitFor(() => expect(motion.fromTo).toHaveBeenCalled()); await vi.waitFor(() => expect(motion.to).toHaveBeenCalled());
  });
  it('does not submit incomplete registration and records complete form state locally', () => { page.onSubmitRegistration(); expect(page.submitted).toBe(false); page.registration = { name: 'Unit adult', age: 25, gender: 'nonbinary', email: 'unit@example.invalid', location: 'Unit city' }; page.onSubmitRegistration(); expect(page.submitted).toBe(true); });
});
