import { ChangeDetectorRef } from '@angular/core';
import { vi } from 'vitest';
const originalMatchMedia = vi.hoisted(() => {
  const descriptor = Object.getOwnPropertyDescriptor(window, 'matchMedia');
  Object.defineProperty(window, 'matchMedia', { configurable: true, value: vi.fn((media: string) => ({
    media, matches: false, onchange: null, addListener: vi.fn(), removeListener: vi.fn(),
    addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
  })) });
  return descriptor;
});
import { LandingCleanComponent } from './landing-clean.component';
import { LandingFinalRealComponent } from '../landing-final-real/landing-final-real.component';
import { Landing3DRealComponent } from '../landing-3d-real/landing-3d-real.component';

describe('Landing prototype registration and server safety contracts', () => {
  afterEach(() => vi.restoreAllMocks());
  afterAll(() => {
    if (originalMatchMedia) Object.defineProperty(window, 'matchMedia', originalMatchMedia);
    else delete (window as any).matchMedia;
  });
  const complete = { name: 'Synthetic Adult', age: 25, gender: 'nonbinary', email: 'synthetic@example.invalid', location: 'Synthetic City' };
  for (const type of [LandingCleanComponent, LandingFinalRealComponent]) {
    for (const field of ['name', 'age', 'gender', 'email', 'location']) {
      it(`${type.name} leaves an incomplete ${field} submission unconfirmed`, () => {
        const mark = vi.fn(); const page = new type('server', { markForCheck: mark } as unknown as ChangeDetectorRef);
        Object.assign(page.registration, complete, { [field]: field === 'age' ? null : '' });
        page.onSubmitRegistration(); expect(page.submitted).toBe(false); expect(mark).not.toHaveBeenCalled();
      });
    }
    it(`${type.name} acknowledges a complete local demo form without proving account creation`, () => {
      vi.spyOn(console, 'log').mockImplementation(() => {}); const mark = vi.fn();
      const page = new type('server', { markForCheck: mark } as unknown as ChangeDetectorRef);
      Object.assign(page.registration, complete); page.onSubmitRegistration();
      expect(page.submitted).toBe(true); expect(mark).toHaveBeenCalledOnce();
      // Both source forms are prototypes: neither calls an account-creation API.
    });
    it(`${type.name} does not initialize browser graphics during server lifecycle`, () => {
      const page = new type('server', {} as ChangeDetectorRef);
      const init = vi.spyOn(page as any, 'initThreeJS');
      page.ngOnInit(); page.ngAfterViewInit(); page.ngOnDestroy();
      expect(page.isBrowser).toBe(false); expect(init).not.toHaveBeenCalled();
    });
  }
  it('does not initialize the clean landing renderer before the canvas is available', () => {
    const page = new LandingCleanComponent('browser', {} as ChangeDetectorRef);
    const init = vi.spyOn(page as any, 'initThreeJS'); page.ngAfterViewInit(); expect(init).not.toHaveBeenCalled();
  });
  it('keeps the real 3D landing server lifecycle free of renderer creation', () => {
    const page = new Landing3DRealComponent('server');
    page.ngOnInit(); page.ngAfterViewInit(); expect(page.isBrowser).toBe(false);
    expect(() => page.ngOnDestroy()).not.toThrow();
  });
});
