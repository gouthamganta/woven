import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { CommonsPageComponent } from './commons.page';
import { CommonsService, CommonsTile } from '../../services/commons.service';

describe('Rendered Commons feed and drawer controls', () => {
  let api: any;
  const tile = (values: Partial<CommonsTile> = {}): CommonsTile => ({ tileId: 'synthetic-tile', contentType: 'text',
    contentText: 'A synthetic anonymous thought', mediaUrl: null, similarity: 0.7, createdAt: new Date().toISOString(), ...values });
  beforeEach(() => {
    sessionStorage.clear();
    api = { getFeed: vi.fn().mockReturnValue(of({ sessionId: 'synthetic-session', tiles: [] })),
      refresh: vi.fn().mockReturnValue(of({})), recordView: vi.fn().mockReturnValue(of({})),
      createTile: vi.fn().mockReturnValue(of({})), orbitTile: vi.fn().mockReturnValue(of({ mutualDetected: false })) };
  });
  afterEach(() => sessionStorage.clear());
  async function render() {
    TestBed.configureTestingModule({ imports: [CommonsPageComponent], providers: [{ provide: CommonsService, useValue: api }] });
    const fixture = TestBed.createComponent(CommonsPageComponent); fixture.detectChanges(); await fixture.whenStable();
    fixture.componentRef.changeDetectorRef.markForCheck(); fixture.detectChanges(); return fixture;
  }
  it('renders the empty feed and disables posting below the minimum length', async () => {
    const fixture = await render(); expect(fixture.nativeElement.textContent).toContain('Nothing here yet');
    expect(fixture.nativeElement.querySelector('.composeBtn').disabled).toBe(true);
  });
  it('renders loading while the feed response is pending', async () => {
    api.getFeed.mockReturnValue(new Subject()); const fixture = await render();
    expect(fixture.nativeElement.querySelector('.loadState')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.compose')).toBeNull();
  });
  for (const status of [429, 503]) {
    it(`renders the ${status === 429 ? 'daily limit' : 'load error'} response without empty-feed messaging`, async () => {
      api.getFeed.mockReturnValue(throwError(() => ({ status }))); const fixture = await render();
      expect(fixture.nativeElement.textContent).toContain(status === 429 ? 'midnight UTC' : "Couldn't load Commons");
      expect(fixture.nativeElement.textContent).not.toContain('Nothing here yet');
    });
  }
  for (const contentType of ['text', 'photo', 'video']) {
    it(`renders a ${contentType} tile and opens its matching drawer content`, async () => {
      const t = tile({ contentType, mediaUrl: contentType === 'text' ? null : '/synthetic-media', similarity: contentType === 'text' ? 0.7 : 0.2 });
      api.getFeed.mockReturnValue(of({ sessionId: 'synthetic-session', tiles: [t] }));
      const fixture = await render(); fixture.nativeElement.querySelector('.cell').click(); await fixture.whenStable(); fixture.detectChanges();
      const selector = contentType === 'text' ? '.drawerBody' : contentType === 'photo' ? '.drawerImg' : '.drawerVideo';
      expect(fixture.nativeElement.querySelector(selector)).not.toBeNull();
      const button: HTMLButtonElement = fixture.nativeElement.querySelector('.orbitBtn'); expect(button.disabled).toBe(false);
      button.click(); await fixture.whenStable(); fixture.detectChanges(); expect(button.disabled).toBe(true);
      expect(button.textContent).toContain('Orbiting'); expect(api.orbitTile).toHaveBeenCalledWith(t.tileId);
      fixture.nativeElement.querySelector('.closeBtn').click(); await fixture.whenStable(); fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.drawer')).toBeNull(); expect(api.recordView).toHaveBeenCalledOnce();
    });
  }
  it('posts a valid note through the compose control and displays confirmation', async () => {
    const fixture = await render(); const input: HTMLTextAreaElement = fixture.nativeElement.querySelector('textarea');
    input.value = 'A synthetic anonymous post'; input.dispatchEvent(new Event('input')); await fixture.whenStable(); fixture.detectChanges();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('.composeBtn'); expect(button.disabled).toBe(false);
    button.click(); await fixture.whenStable(); fixture.detectChanges();
    expect(api.createTile).toHaveBeenCalledWith('text', 'A synthetic anonymous post');
    expect(fixture.nativeElement.querySelector('.toast').textContent).toContain('Posted anonymously');
    expect(fixture.componentInstance.composeText).toBe('');
  });
  it('offers load more for a full page and hides it after an empty following page', async () => {
    api.getFeed.mockReturnValueOnce(of({ sessionId: 'synthetic-session', tiles: Array.from({ length: 20 }, (_, i) => tile({ tileId: `synthetic-${i}` })) }))
      .mockReturnValue(of({ sessionId: 'synthetic-session', tiles: [] }));
    const fixture = await render(); fixture.nativeElement.querySelector('.loadMoreBtn').click(); await fixture.whenStable(); fixture.detectChanges();
    expect(api.getFeed).toHaveBeenLastCalledWith(2, 'synthetic-session');
    expect(fixture.nativeElement.querySelector('.loadMoreBtn')).toBeNull(); expect(fixture.nativeElement.textContent).toContain('caught up');
  });
  it('shows pending pagination and prevents a second load-more request', async () => {
    const pending = new Subject<any>();
    api.getFeed.mockReturnValueOnce(of({ sessionId: 'synthetic-session', tiles: Array.from({ length: 20 }, (_, i) => tile({ tileId: `synthetic-${i}` })) }))
      .mockReturnValue(pending);
    const fixture = await render(); fixture.nativeElement.querySelector('.loadMoreBtn').click();
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.loadState.small')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.loadMoreBtn')).toBeNull();
    await fixture.componentInstance.loadMore(); expect(api.getFeed).toHaveBeenCalledTimes(2);
    pending.next({ sessionId: 'synthetic-session', tiles: [] }); pending.complete(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.loadState.small')).toBeNull();
  });
  it('disables the orbit control while its response is pending and preserves modal clicks', async () => {
    const pending = new Subject<any>(); api.orbitTile.mockReturnValue(pending);
    api.getFeed.mockReturnValue(of({ sessionId: 'synthetic-session', tiles: [tile()] }));
    const fixture = await render(); fixture.nativeElement.querySelector('.cell').click(); await fixture.whenStable(); fixture.detectChanges();
    fixture.nativeElement.querySelector('.drawer').click(); expect(fixture.componentInstance.activeTile).not.toBeNull();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('.orbitBtn'); button.click();
    await fixture.whenStable(); fixture.detectChanges(); expect(button.disabled).toBe(true); expect(button.textContent).not.toContain('Orbiting');
    pending.next({ mutualDetected: true }); pending.complete(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.toast').textContent).toContain('mutual pull');
  });
  it('warns near the text limit and prevents a second post while saving', async () => {
    const pending = new Subject<any>(); api.createTile.mockReturnValue(pending); const fixture = await render();
    const input: HTMLTextAreaElement = fixture.nativeElement.querySelector('textarea');
    input.value = 'x'.repeat(131); input.dispatchEvent(new Event('input')); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.composeCount').classList.contains('warn')).toBe(true);
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('.composeBtn'); button.click();
    await fixture.whenStable(); fixture.detectChanges(); expect(button.disabled).toBe(true);
    await fixture.componentInstance.postTile(); expect(api.createTile).toHaveBeenCalledOnce();
    pending.next({}); pending.complete(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.componentInstance.posting).toBe(false);
  });
  it('animates the refresh control until the replacement feed has loaded', async () => {
    const pending = new Subject<any>(); api.refresh.mockReturnValue(pending); const fixture = await render();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('.refreshBtn'); button.click();
    await fixture.whenStable(); fixture.detectChanges(); expect(button.classList.contains('spinning')).toBe(true);
    button.click(); expect(api.refresh).toHaveBeenCalledOnce(); pending.next({}); pending.complete();
    await fixture.whenStable(); fixture.detectChanges(); expect(button.classList.contains('spinning')).toBe(false);
    expect(api.getFeed).toHaveBeenCalledTimes(2);
  });
});
