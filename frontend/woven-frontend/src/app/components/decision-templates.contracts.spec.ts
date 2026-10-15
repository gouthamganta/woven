import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ChatNoteOverlayComponent } from '../pages/moments/chat-note-overlay.component';
import { InlineGamePlayerComponent } from './inline-game-player/inline-game-player.component';
import { GamesService } from '../services/games.service';

describe('Rendered ChatNote controls', () => {
  function render(card: unknown = { userId: 1, fullName: 'Synthetic Adult' }, choice = 'MAGICAL') {
    TestBed.configureTestingModule({ imports: [ChatNoteOverlayComponent] });
    const fixture = TestBed.createComponent(ChatNoteOverlayComponent);
    fixture.componentRef.setInput('card', card); fixture.componentRef.setInput('choice', choice); fixture.detectChanges();
    return fixture;
  }
  it('renders nothing when no card is selected', () => {
    expect(render(null).nativeElement.querySelector('.overlay')).toBeNull();
  });
  for (const choice of ['MAGICAL', 'LOGICAL']) {
    it(`renders ${choice} identity and emits the valid note from a real Send click`, async () => {
      const fixture = render({ userId: 1, fullName: 'Synthetic Adult', profilePhoto: '/synthetic.png' }, choice);
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('img')!.alt).toBe('Synthetic Adult');
      expect(el.querySelector('.choiceBadge')!.classList.contains(choice.toLowerCase())).toBe(true);
      const submit = vi.fn(); fixture.componentInstance.submitted.subscribe(submit);
      const input = el.querySelector('textarea')!;
      input.value = '  A synthetic note longer than twenty characters  '; input.dispatchEvent(new Event('input'));
      await fixture.whenStable(); fixture.detectChanges();
      const send = el.querySelector<HTMLButtonElement>('.sendBtn')!; expect(send.disabled).toBe(false); send.click();
      expect(submit).toHaveBeenCalledWith('A synthetic note longer than twenty characters');
    });
  }
  it('shows the fallback initial and keeps short notes disabled with a remaining-length hint', async () => {
    const fixture = render(); const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.photoFallback')!.textContent).toBe('S');
    const input = el.querySelector('textarea')!; input.value = 'short'; input.dispatchEvent(new Event('input'));
    await fixture.whenStable(); fixture.detectChanges();
    expect(el.querySelector('.minHint')!.textContent).toContain('15 more');
    expect(el.querySelector<HTMLButtonElement>('.sendBtn')!.disabled).toBe(true);
  });
  it('opens starter choices, applies a starter, and closes the dropdown', async () => {
    const fixture = render(); const el: HTMLElement = fixture.nativeElement;
    el.querySelector<HTMLButtonElement>('.starterTrigger')!.click(); fixture.detectChanges();
    expect(el.querySelectorAll('.starterItem')).toHaveLength(3);
    el.querySelector<HTMLButtonElement>('.starterItem')!.click(); fixture.detectChanges(); await fixture.whenStable();
    expect(fixture.componentInstance.noteText).toBe(fixture.componentInstance.starters[0]);
    expect(el.querySelector('.starterList')).toBeNull();
  });
  it('applies the suggested bridge question from its button', async () => {
    const question = 'What synthetic topic would you like to explore?';
    const fixture = render({ userId: 1, fullName: 'Synthetic Adult', bridgeQuestion: question });
    fixture.nativeElement.querySelector('.bridgeQuestion').click(); fixture.detectChanges(); await fixture.whenStable();
    expect(fixture.componentInstance.noteText).toBe(question);
  });
  it('warns near the character limit and disables the button during submission', async () => {
    const fixture = render(); fixture.componentInstance.noteText = 'x'.repeat(131); fixture.componentInstance.submitting = true;
    fixture.componentRef.changeDetectorRef.markForCheck(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.charCount').classList.contains('warn')).toBe(true);
    expect(fixture.nativeElement.querySelector('.sendBtn').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('.sendBtn').textContent).toContain('Sending');
  });
  it('emits Back and clears the note through the accessible Back button', () => {
    const fixture = render(); const back = vi.fn(); fixture.componentInstance.back.subscribe(back);
    fixture.componentInstance.noteText = 'synthetic draft'; fixture.nativeElement.querySelector('[aria-label="Back"]').click();
    expect(back).toHaveBeenCalledOnce(); expect(fixture.componentInstance.noteText).toBe('');
  });
});

describe('Rendered inline game controls', () => {
  let api: any;
  beforeEach(() => {
    api = { getCurrentRound: vi.fn(), submitGuesses: vi.fn().mockReturnValue(of({ status: 'UNCHANGED' })),
      submitTargetAnswers: vi.fn().mockReturnValue(of({ status: 'UNCHANGED' })) };
  });
  const round = (isGuesser: boolean) => ({ roundNumber: 1, totalRounds: 3, isGuesser, hasAnswered: false,
    questions: [{ id: 'q1', text: 'Their ideal afternoon?', options: ['Outside', { id: 'inside', text: 'Inside' }] }] });
  function render() {
    TestBed.configureTestingModule({ imports: [InlineGamePlayerComponent], providers: [{ provide: GamesService, useValue: api }] });
    const fixture = TestBed.createComponent(InlineGamePlayerComponent);
    fixture.componentRef.setInput('sessionId', 'synthetic-session'); fixture.detectChanges(); return fixture;
  }
  it('shows preparation while the round response is pending', () => {
    api.getCurrentRound.mockReturnValue(new Subject()); const fixture = render();
    expect(fixture.nativeElement.textContent).toContain('Preparing your round');
    expect(fixture.nativeElement.querySelector('.submitButton')).toBeNull();
  });
  for (const isGuesser of [true, false]) {
    it(`renders the ${isGuesser ? 'guesser' : 'target'} role and submits the selected option through the actual button`, () => {
      api.getCurrentRound.mockReturnValue(of(round(isGuesser))); const fixture = render(); const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('.roleLabel')!.textContent).toContain(isGuesser ? 'Guess' : 'Answer');
      expect(el.querySelector('.qText')!.textContent).toContain(isGuesser ? 'Their' : 'Your');
      const send = el.querySelector<HTMLButtonElement>('.submitButton')!; expect(send.disabled).toBe(true);
      el.querySelectorAll<HTMLButtonElement>('.optionItem')[1].click();
      fixture.componentRef.changeDetectorRef.markForCheck(); fixture.detectChanges();
      expect(send.disabled).toBe(false); expect(el.querySelectorAll('.optionItem.chosen')).toHaveLength(1); send.click();
      expect(isGuesser ? api.submitGuesses : api.submitTargetAnswers).toHaveBeenCalledWith('synthetic-session', { q1: 'Inside' });
    });
  }
  it('renders a failure and invokes loadRound from the retry control', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    api.getCurrentRound.mockReturnValueOnce(throwError(() => new Error('synthetic load failure'))).mockReturnValue(of(round(true)));
    const fixture = render(); expect(fixture.nativeElement.textContent).toContain('Could not load round');
    fixture.nativeElement.querySelector('.retryButton').click(); fixture.detectChanges();
    expect(api.getCurrentRound).toHaveBeenCalledTimes(2); expect(fixture.nativeElement.querySelector('.retryButton')).toBeNull();
    vi.restoreAllMocks();
  });
  it('shows the waiting state after the other player is required', async () => {
    api.getCurrentRound.mockReturnValue(of({ ...round(false), hasAnswered: true })); const fixture = render();
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Waiting for them');
    expect(fixture.nativeElement.querySelector('.questionsContainer')).toBeNull();
  });
  it('keeps modal clicks inside and emits close from the overlay', () => {
    api.getCurrentRound.mockReturnValue(of(round(true))); const fixture = render(); const close = vi.fn();
    fixture.componentInstance.close.subscribe(close); fixture.nativeElement.querySelector('.gameModal').click();
    expect(close).not.toHaveBeenCalled(); fixture.nativeElement.querySelector('.gameOverlay').click(); expect(close).toHaveBeenCalledOnce();
  });
});
