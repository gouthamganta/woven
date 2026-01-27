import { Component, ElementRef, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MomentsService, PendingResponse, PendingCard, MomentsResponse } from '../../services/moments.service';
import { ChatService } from '../../services/chat.service';

@Component({
  selector: 'app-pending-moments-page',
  standalone: true,
  imports: [CommonModule, DatePipe],
  templateUrl: './pending.page.html',
  styleUrls: ['./pending.page.scss'],
})
export class PendingMomentsPageComponent implements OnInit {
  loading = true;
  error = '';

  pending: PendingResponse | null = null;
  meta: MomentsResponse | null = null;

  toast = '';
  index = 0;

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

  async load() {
    this.loading = true;
    this.error = '';
    this.cdr.detectChanges(); // ✅ render loading immediately

    try {
      const [pending, meta] = await Promise.all([
        firstValueFrom(this.moments.getPending()),
        firstValueFrom(this.moments.getMoments()),
      ]);

      this.pending = pending;
      this.meta = meta;
      this.index = 0;

      this.cdr.detectChanges(); // ✅ render results immediately

      setTimeout(() => this.snapToIndex(0), 0);
    } catch {
      this.error = 'Could not load Saved. Try again.';
      this.cdr.detectChanges();
    } finally {
      this.loading = false;
      this.cdr.detectChanges();
    }
  }

  back() {
    this.router.navigateByUrl('/home/moments');
  }

  get themeTitle(): string {
    const t = this.meta?.theme;
    if (!t) return '';
    return `${t.left.label} / ${t.right.label}`;
  }

  get cards(): PendingCard[] {
    return this.pending?.cards ?? [];
  }

  get card(): PendingCard | null {
    return this.cards?.[this.index] ?? null;
  }

  get countText(): string {
    const total = this.cards.length;
    if (!total) return '';
    return `${this.index + 1} / ${total}`;
  }

  get budgetLeftText(): string {
    const b = this.meta?.budget;
    if (!b) return '';
    return `${b.pendingRemaining} saved • ${b.totalRemaining} sparks`;
  }

  cardMetaLine(card: PendingCard): string {
    const ageText = card.age != null ? `${card.age}` : '';
    const city = (card.location?.city || '').toString().trim();
    const state = (card.location?.state || '').toString().trim();
    const locText = [city, state].filter(Boolean).join(', ');
    return [ageText, locText].filter(Boolean).join(' • ');
  }

  onDeckScroll() {
    const el = this.deck?.nativeElement;
    if (!el) return;
    const w = el.clientWidth;
    if (!w) return;
    const i = Math.round(el.scrollLeft / w);
    this.index = Math.max(0, Math.min(i, this.cards.length - 1));
  }

  snapToIndex(i: number) {
    const el = this.deck?.nativeElement;
    if (!el) return;
    el.scrollTo({ left: i * el.clientWidth, behavior: 'smooth' });
  }

  async decide(card: PendingCard, choice: 'YES' | 'NO') {
    try {
      const res = await firstValueFrom(
        this.moments.respond({
          targetUserId: card.userId,
          choice,
          source: 'PENDING',
        })
      );

      if (res?.matchId && (res.status === 'PURE_MATCH_CREATED' || res.status === 'EDGE_MATCH_CREATED')) {
        this.showToast('Balloon opened 🎈');
        const started = await firstValueFrom(this.chat.start(res.matchId));
        this.router.navigateByUrl(`/home/chats/${started.threadId}`);
        return;
      }

      this.showToast('Noted.');

      if (this.pending) {
        const nextCards = (this.pending.cards || []).filter((x) => x.userId !== card.userId);
        this.pending = { count: nextCards.length, cards: nextCards };
      }

      this.meta = await firstValueFrom(this.moments.getMoments());
      this.cdr.detectChanges();

      const nextIndex = Math.min(this.index, (this.pending?.cards?.length ?? 1) - 1);
      this.index = Math.max(0, nextIndex);
      setTimeout(() => this.snapToIndex(this.index), 0);
    } catch {
      this.showToast('Could not save that. Try again.');
    } finally {
      this.cdr.detectChanges();
    }
  }

  private showToast(msg: string) {
    this.toast = msg;
    this.cdr.detectChanges();
    window.setTimeout(() => {
      this.toast = '';
      this.cdr.detectChanges();
    }, 1400);
  }
}
