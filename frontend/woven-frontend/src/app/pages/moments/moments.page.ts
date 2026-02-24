import {
  Component,
  ElementRef,
  OnInit,
  ViewChild,
  ChangeDetectorRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  MomentsService,
  MomentsResponse,
  MomentsCard,
} from '../../services/moments.service';
import { ChatService } from '../../services/chat.service';

type MomentUiChoice = 'LEFT' | 'HOLD' | 'RIGHT';

@Component({
  selector: 'app-moments-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './moments.page.html',
  styleUrls: ['./moments.page.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MomentsPageComponent implements OnInit {
  loading = true;
  error = '';
  data: MomentsResponse | null = null;

  index = 0;
  pendingCount = 0;
  toast = '';
  showCoach = true;
  showLearnMore = false;

  // ✅ NEW: Track which cards have been responded to (so we can filter them out)
  respondedUserIds = new Set<number>();

  @ViewChild('deck', { static: false }) deck?: ElementRef<HTMLDivElement>;

  constructor(
    private moments: MomentsService,
    private chat: ChatService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  async ngOnInit() {
    await this.load();
  }

  // ✅ FIXED: Filter out cards that user has already responded to
  get cards(): MomentsCard[] {
    return (this.data?.cards ?? []).filter(
      (c) => !this.respondedUserIds.has(c.userId)
    );
  }

  get card(): MomentsCard | null {
    return this.cards?.[this.index] ?? null;
  }

  get theme() {
    return this.data?.theme ?? null;
  }

  get remainingText(): string {
    const b = this.data?.budget;
    if (!b) return '';
    return `${b.totalRemaining} sparks left`;
  }

  get headline(): string {
    const t = this.theme;
    if (!t) return '';
    return `${t.left.label} / ${t.right.label}`;
  }

  get microLine(): string {
    return 'One hypothetical date. What would it be?';
  }

  // ✅ used by template
  locationLine(c: MomentsCard): string {
    const city = (c.location?.city ?? '').toString().trim();
    const state = (c.location?.state ?? '').toString().trim();
    return [city, state].filter((v) => !!v).join(', ');
  }

  getRatingBarFill(side: 'red' | 'green', barNumber: number, average: number): boolean {
    if (side === 'red') {
      if (average >= 0) return false;
      const threshold = -25 * barNumber;
      return average <= threshold;
    } else {
      if (average <= 0) return false;
      const threshold = 25 * barNumber;
      return average >= threshold;
    }
  }

  async load() {
    this.loading = true;
    this.error = '';
    this.cdr.detectChanges();

    try {
      const [moments, pending] = await Promise.all([
        firstValueFrom(this.moments.getMoments()),
        firstValueFrom(this.moments.getPending()),
      ]);

      this.data = moments;
      this.pendingCount = pending?.count ?? 0;
      this.index = 0;
      this.showCoach = true;
      // ✅ Reset responded set on reload
      this.respondedUserIds.clear();

      this.cdr.detectChanges();
    } catch {
      this.error = 'Could not load today\'s vibe.';
      this.cdr.detectChanges();
    } finally {
      this.loading = false;
      this.cdr.detectChanges();

      // ✅ snap after DOM paints
      setTimeout(() => this.snapToIndex(0), 0);
    }
  }

  openPending() {
    this.router.navigateByUrl('/home/moments/pending');
  }

  toggleCoach() {
    this.showCoach = !this.showCoach;
  }

  toggleLearnMore() {
    this.showLearnMore = !this.showLearnMore;
  }

  onDeckScroll() {
    const el = this.deck?.nativeElement;
    if (!el) return;
    const cardW = el.clientWidth;
    if (!cardW) return;

    const i = Math.round(el.scrollLeft / cardW);
    this.index = Math.max(0, Math.min(i, this.cards.length - 1));
  }

  snapToIndex(i: number) {
    const el = this.deck?.nativeElement;
    if (!el) return;
    el.scrollTo({ left: i * el.clientWidth, behavior: 'smooth' });
  }

  async choose(uiChoice: MomentUiChoice) {
    if (!this.card || !this.theme) return;

    const currentCard = this.card; // ✅ Capture current card before it changes

    const backendChoice =
      uiChoice === 'LEFT'
        ? this.theme.left.choice
        : uiChoice === 'HOLD'
        ? this.theme.mid.choice
        : this.theme.right.choice;

    try {
      const res = await firstValueFrom(
        this.moments.respond({
          targetUserId: currentCard.userId,
          choice: backendChoice,
          source: null,
        })
      );

      // ✅ Mark this card as responded IMMEDIATELY after successful response
      this.respondedUserIds.add(currentCard.userId);

      if (
        res?.matchId &&
        (res.status === 'PURE_MATCH_CREATED' ||
          res.status === 'EDGE_MATCH_CREATED')
      ) {
        this.showToast('Balloon opened 🎈');
        const started = await firstValueFrom(this.chat.start(res.matchId));
        this.router.navigateByUrl(`/home/chats/${started.threadId}`);
        return;
      }

      if (uiChoice === 'HOLD') {
        this.pendingCount++;
        this.showToast('Saved for later ✨');
      } else {
        this.showToast('Noted.');
      }

      // ✅ FIXED: After filtering, adjust index if needed
      // If we're not at the last card, stay at current index (next card slides in)
      // If we just responded to the last card, move back one
      this.cdr.detectChanges(); // Force update so cards getter recalculates

      if (this.index >= this.cards.length && this.cards.length > 0) {
        // We were at the end, move back
        this.index = this.cards.length - 1;
      }

      // Snap to current index (which now shows the next card)
      setTimeout(() => this.snapToIndex(this.index), 100);
    } catch {
      this.showToast('Could not save that. Try again.');
    } finally {
      this.cdr.detectChanges();
    }
  }

  private showToast(msg: string) {
    this.toast = msg;
    this.cdr.detectChanges();

    // SSR-safe
    if (typeof window === 'undefined') return;

    window.setTimeout(() => {
      this.toast = '';
      this.cdr.detectChanges();
    }, 1400);
  }
}