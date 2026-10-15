import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ProfilePageComponent } from './profile/profile';
import { SettingsPageComponent } from './settings/settings';
import { MyTilesPageComponent } from './my-tiles/my-tiles.page';
import { OnboardingService } from '../onboarding/onboarding.service';
import { TilesService, MyTile } from '../services/tiles.service';
import { PushService } from '../services/push.service';
import { CoachingService } from '../services/coaching.service';

describe('Rendered account and tile controls', () => {
  let onboarding: { getReview: ReturnType<typeof vi.fn> };
  let tiles: Record<string, ReturnType<typeof vi.fn>>;
  beforeEach(() => {
    onboarding = { getReview: vi.fn().mockReturnValue(of({ publicPreview: { name: 'Synthetic Adult', photos: [] } })) };
    tiles = { getMine: vi.fn().mockReturnValue(of({ tiles: [] })), getReceivedOrbits: vi.fn().mockReturnValue(of([])), create: vi.fn().mockReturnValue(of({})), highlight: vi.fn().mockReturnValue(of({})), unhighlight: vi.fn().mockReturnValue(of({})) };
  });
  afterEach(() => { vi.restoreAllMocks(); });
  const tile = (id: string, overrides: Partial<MyTile> = {}): MyTile => ({ id, contentType: 'text', contentText: 'Unit tile content', mediaUrl: null, createdAt: '2026-10-09T12:00:00Z', expiresAt: '2099-10-09T12:00:00Z', isExpired: false, isHighlighted: false, isModerated: false, highlightSlot: null, ...overrides });
  async function render<T>(type: Type<T>) {
    TestBed.configureTestingModule({ imports: [type], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
      { provide: OnboardingService, useValue: onboarding }, { provide: TilesService, useValue: tiles },
      { provide: PushService, useValue: { isSupported: async () => false, isSubscribed: async () => false } },
      { provide: CoachingService, useValue: { optIn: () => of({}), optOut: () => of({}) } },
    ] });
    const fixture = TestBed.createComponent(type); fixture.detectChanges(); await fixture.whenStable();
    if ('loading' in (fixture.componentInstance as object)) {
      await vi.waitFor(() => expect((fixture.componentInstance as { loading: boolean }).loading).toBe(false));
    }
    fixture.componentRef.changeDetectorRef.markForCheck(); fixture.detectChanges(); return fixture;
  }

  it('renders profile fallback initials without inventing a photo', async () => {
    const fixture = await render(ProfilePageComponent);
    expect(fixture.nativeElement.querySelector('.heroFallback')?.textContent).toBe('S');
    expect(fixture.nativeElement.querySelector('.heroImg')).toBeNull(); expect(fixture.nativeElement.textContent).toContain('Synthetic Adult');
  });

  it('renders supplied photos, highlights, pronouns, intent and public details', async () => {
    onboarding.getReview.mockReturnValue(of({ publicPreview: { name: 'Synthetic Adult', isVerified: true, location: 'Unit City', displayPronouns: 'they/them', bio: 'Unit bio', intent: { primaryIntent: 'friendship', openness: ['long_term'] }, optionalPublic: [{ key: 'job', value: 'Engineer' }], photos: [{ url: '/one', sortOrder: 1 }, { url: '/two', sortOrder: 2 }] } }));
    tiles['getMine'].mockReturnValue(of({ tiles: [tile('text-highlight', { isHighlighted: true, highlightSlot: 1 }), tile('image-highlight', { isHighlighted: true, highlightSlot: 2, mediaUrl: '/tile' }), tile('recent'), tile('image-recent', { mediaUrl: '/recent' })] }));
    const fixture = await render(ProfilePageComponent); const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('.heroImg')?.getAttribute('src')).toBe('/one'); expect(element.querySelectorAll('.thumb')).toHaveLength(2);
    expect(element.querySelectorAll('.tilesRow')).toHaveLength(2); expect(element.textContent).toContain('they/them'); expect(element.textContent).toContain('Unit bio'); expect(element.textContent).toContain('Engineer');
  });

  it('renders the actual profile error state after a rejected load', async () => {
    onboarding.getReview.mockReturnValue(throwError(() => new Error('Unit failure')));
    const fixture = await render(ProfilePageComponent); expect(fixture.nativeElement.querySelector('.errState')?.textContent).toContain("Couldn't load"); expect(fixture.nativeElement.querySelector('.loading')).toBeNull();
  });

  it('renders settings and disables notification registration for an unsupported browser', async () => {
    const fixture = await render(SettingsPageComponent); const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('Settings'); expect(element.querySelector<HTMLButtonElement>('.toggle')?.disabled).toBe(true);
    expect(element.querySelector('a[href="/privacy"]')).not.toBeNull(); expect(element.querySelector('a[href="/terms"]')).not.toBeNull();
  });

  it('opens and cancels deletion through the actual rendered buttons without sending a deletion request', async () => {
    const fixture = await render(SettingsPageComponent); const element: HTMLElement = fixture.nativeElement;
    element.querySelector<HTMLButtonElement>('.row.danger')!.click(); fixture.detectChanges(); expect(element.textContent).toContain('Delete your account?');
    const buttons = [...element.querySelectorAll<HTMLButtonElement>('.sheet button')]; const cancel = buttons.find(b => /cancel|keep|back/i.test(b.textContent || ''));
    expect(cancel).toBeDefined(); cancel!.click(); fixture.detectChanges(); expect(element.querySelector('.overlay')).toBeNull();
  });

  it('renders block placeholders, busy unblocking and both privacy-safe avatar states', async () => {
    const fixture = await render(SettingsPageComponent); const page = fixture.componentInstance;
    page.showBlocks = true; page.loadingBlocks = true; page.cdr.markForCheck(); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('Loading');
    page.loadingBlocks = false; page.cdr.markForCheck(); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('No blocked users');
    page.blockedUsers = [{ userId: 1, name: 'Unit A', photo: null, blockedAt: '2026-10-09' }, { userId: 2, name: 'Unit B', photo: '/unit-photo', blockedAt: '2026-10-09' }]; page.unblocking = 1; page.cdr.markForCheck(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.blockRow')).toHaveLength(2); expect(fixture.nativeElement.querySelector('.blockInitial')?.textContent).toBe('U'); expect(fixture.nativeElement.querySelector('.blockAvatar img')?.getAttribute('src')).toBe('/unit-photo'); expect(fixture.nativeElement.querySelector('.unblockBtn').disabled).toBe(true);
  });

  it('renders the empty tile state and enables posting only for a valid draft', async () => {
    const fixture = await render(MyTilesPageComponent); const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('No tiles yet'); expect(element.querySelector<HTMLButtonElement>('.composeBtn')!.disabled).toBe(true);
    const input = element.querySelector<HTMLTextAreaElement>('textarea')!; input.value = 'Unit tile text'; input.dispatchEvent(new Event('input')); await fixture.whenStable(); fixture.detectChanges();
    expect(element.querySelector<HTMLButtonElement>('.composeBtn')!.disabled).toBe(false); element.querySelector<HTMLButtonElement>('.composeBtn')!.click(); await fixture.whenStable(); fixture.detectChanges();
    expect(tiles['create']).toHaveBeenCalledWith('text', 'Unit tile text'); expect(input.value).toBe('');
  });

  it('renders highlighted, active and expired tiles and the pin-slot picker', async () => {
    const data = [tile('pinned', { isHighlighted: true, highlightSlot: 1, mediaUrl: '/pinned' }), tile('active'), tile('expired', { isExpired: true, mediaUrl: '/expired' })];
    tiles['getMine'].mockReturnValue(of({ tiles: data })); tiles['getReceivedOrbits'].mockReturnValue(of([{ tileId: 'pinned' }, { tileId: 'active' }, { tileId: 'expired' }]));
    const fixture = await render(MyTilesPageComponent); const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('HIGHLIGHTED'); expect(element.textContent).toContain('ACTIVE'); expect(element.textContent).toContain('EXPIRED'); expect(element.querySelectorAll('.tileCard')).toHaveLength(3);
    const pin = element.querySelector<HTMLButtonElement>('.pinBtn');
    expect(pin).not.toBeNull(); pin!.click(); fixture.detectChanges(); expect(fixture.componentInstance.pickingFor?.id).toBe('expired');
    expect(element.textContent).toContain('slot'); fixture.componentInstance.closeSlotPicker(); fixture.detectChanges(); expect(fixture.componentInstance.pickingFor).toBeNull();
  });

  it('shows tile loading controls while the request is unresolved', async () => {
    const pending = new Subject<{ tiles: MyTile[] }>(); tiles['getMine'].mockReturnValue(pending);
    TestBed.configureTestingModule({ imports: [MyTilesPageComponent], providers: [provideRouter([]), { provide: TilesService, useValue: tiles }] });
    const fixture = TestBed.createComponent(MyTilesPageComponent); fixture.detectChanges(); expect(fixture.nativeElement.querySelector('.loadState')).not.toBeNull();
    pending.next({ tiles: [] }); pending.complete(); await vi.waitFor(() => expect(fixture.componentInstance.loading).toBe(false));
    fixture.componentRef.changeDetectorRef.markForCheck(); fixture.detectChanges(); expect(fixture.nativeElement.querySelector('.loadState')).toBeNull();
  });
});
