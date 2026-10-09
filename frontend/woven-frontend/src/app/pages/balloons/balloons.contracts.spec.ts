import { ChangeDetectorRef } from '@angular/core';
import { Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { BalloonsPageComponent } from './balloons.page';
import { MatchesService, MatchListItem } from '../../services/matches.service';
import { ChatService } from '../../services/chat.service';

describe('Balloon list timing and action contracts', () => {
  let page: BalloonsPageComponent;
  let list: ReturnType<typeof vi.fn>;
  let pop: ReturnType<typeof vi.fn>;
  let start: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.fn>;
  const match = (values: Partial<MatchListItem> = {}) => ({ matchId: 'synthetic-match', ...values } as MatchListItem);
  beforeEach(() => {
    vi.useFakeTimers(); list = vi.fn().mockReturnValue(of({ matches: [] }));
    pop = vi.fn().mockReturnValue(of({})); start = vi.fn().mockReturnValue(of({ threadId: 'synthetic-thread' })); navigate = vi.fn();
    page = new BalloonsPageComponent({ list, pop } as unknown as MatchesService, { start } as unknown as ChatService,
      { navigateByUrl: navigate } as unknown as Router, { markForCheck: vi.fn() } as unknown as ChangeDetectorRef);
  });
  afterEach(() => { page.ngOnDestroy(); vi.clearAllTimers(); vi.useRealTimers(); });
  it('loads the returned matches and uses singular and plural count text', async () => {
    expect(page.countText).toBe('0 balloons'); list.mockReturnValue(of({ matches: [match()] }));
    await page.load(); expect(page.matches).toHaveLength(1); expect(page.countText).toBe('1 balloon');
    page.matches.push(match({ matchId: 'synthetic-second' })); expect(page.countText).toBe('2 balloons');
    expect(page.loading).toBe(false);
  });
  it('accepts an empty payload as an empty match list', async () => {
    list.mockReturnValue(of(null)); await page.load(); expect(page.matches).toEqual([]); expect(page.error).toBe('');
  });
  it('clears a load error after a successful retry', async () => {
    list.mockReturnValueOnce(throwError(() => new Error('synthetic outage')));
    await page.load(); expect(page.error).toBe('Could not load balloons.'); expect(page.loading).toBe(false);
    await page.load(); expect(page.error).toBe('');
  });
  it('shows waiting state before the first reply and falls back to Someone without a profile', () => {
    const m = match(); expect(page.headline(m)).toBe('Someone'); expect(page.statusLine(m)).toContain('Waiting');
    expect(page.showFindLove(m)).toBe(false); expect(page.findLoveChip(m)).toBe('Balloon');
  });
  it('counts down to opening and opens at the exact threshold', () => {
    page.now = Date.UTC(2026, 0, 1);
    const m = match({ findLoveAt: new Date(page.now + 65000).toISOString() });
    expect(page.statusLine(m)).toBe('Balloon opens in 01:05'); expect(page.findLoveChip(m)).toBe('Opens 01:05');
    expect(page.showFindLove(m)).toBe(false); page.now += 65000;
    expect(page.showFindLove(m)).toBe(true); expect(page.findLoveChip(m)).toBe('Find Love');
    expect(page.statusLine(m)).toContain('Find Love is open');
  });
  it('updates time once per second and stops the clock at destruction', async () => {
    await page.ngOnInit(); const previous = page.now; vi.advanceTimersByTime(1000); expect(page.now).toBe(previous + 1000);
    page.ngOnDestroy(); const stopped = page.now; vi.advanceTimersByTime(2000); expect(page.now).toBe(stopped);
  });
  it('starts the selected chat and routes to the returned thread', async () => {
    await page.open(match()); expect(start).toHaveBeenCalledWith('synthetic-match');
    expect(navigate).toHaveBeenCalledWith('/chats/synthetic-thread');
  });
  it('does not navigate when chat startup fails', async () => {
    start.mockReturnValue(throwError(() => new Error('synthetic startup failure')));
    await page.open(match()); expect(navigate).not.toHaveBeenCalled();
  });
  it('prevents duplicate pop actions while pending and reloads after success', async () => {
    const pending = new Subject<unknown>(); pop.mockReturnValue(pending); const event = { stopPropagation: vi.fn() } as unknown as MouseEvent;
    const m = match(); const first = page.pop(m, event); expect(page.isBusy(m)).toBe(true);
    await page.pop(m, event); expect(pop).toHaveBeenCalledOnce(); expect(list).not.toHaveBeenCalled();
    pending.next({}); pending.complete(); await first; expect(list).toHaveBeenCalledOnce();
    expect(page.isBusy(m)).toBe(false); expect(event.stopPropagation).toHaveBeenCalledTimes(2);
  });
  it('releases the action lock and shows an error when popping fails', async () => {
    pop.mockReturnValue(throwError(() => new Error('synthetic pop failure')));
    const m = match(); await page.pop(m, { stopPropagation: vi.fn() } as unknown as MouseEvent);
    expect(page.error).toBe('Could not pop balloon.'); expect(page.isBusy(m)).toBe(false); expect(list).not.toHaveBeenCalled();
  });
  it('stops event bubbling and routes profile inspection to the selected match', () => {
    const stopPropagation = vi.fn(); page.viewProfile(match(), { stopPropagation } as unknown as MouseEvent);
    expect(stopPropagation).toHaveBeenCalledOnce(); expect(navigate).toHaveBeenCalledWith('/matches/synthetic-match/profile');
  });
});
