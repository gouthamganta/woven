import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MatchesService, MatchListItem } from '../../services/matches.service';
import { ChatService } from '../../services/chat.service';

@Component({
  selector: 'app-balloons-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './balloons.page.html',
  styleUrls: ['./balloons.page.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BalloonsPageComponent implements OnInit, OnDestroy {
  loading = true;
  error = '';
  matches: MatchListItem[] = [];

  now = Date.now();
  private t?: any;
  private busyId: string | null = null;

  constructor(
    private matchesApi: MatchesService,
    private chat: ChatService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  async ngOnInit() {
    await this.load();
    this.t = window.setInterval(() => { this.now = Date.now(); this.cdr.markForCheck(); }, 1000);
  }

  ngOnDestroy() {
    if (this.t) window.clearInterval(this.t);
  }

  async load() {
    this.loading = true;
    this.error = '';
    try {
      const res = await firstValueFrom(this.matchesApi.list());
      this.matches = res?.matches ?? [];
    } catch {
      this.error = 'Could not load balloons.';
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
    }
  }

  headline(m: MatchListItem) {
    return `${m.other?.fullName ?? 'Someone'}`;
  }

  statusLine(m: MatchListItem): string {
    if (!m.findLoveAt) return 'Waiting for their first reply…';

    const ms = new Date(m.findLoveAt).getTime() - this.now;
    if (ms <= 0) return 'Find Love is open 💗';

    return `Balloon opens in ${this.mmss(ms)}`;
  }

  showFindLove(m: MatchListItem): boolean {
    if (!m.findLoveAt) return false;
    return new Date(m.findLoveAt).getTime() <= this.now;
  }

  findLoveChip(m: MatchListItem): string {
    if (!m.findLoveAt) return 'Balloon';
    const ms = new Date(m.findLoveAt).getTime() - this.now;
    if (ms <= 0) return 'Find Love';
    return `Opens ${this.mmss(ms)}`;
  }

  private mmss(ms: number): string {
    const s = Math.max(0, Math.floor(ms / 1000));
    const mm = Math.floor(s / 60);
    const ss = s % 60;
    return `${mm.toString().padStart(2, '0')}:${ss.toString().padStart(2, '0')}`;
  }

  async open(m: MatchListItem) {
    try {
      const started = await firstValueFrom(this.chat.start(m.matchId));
      this.router.navigateByUrl(`/home/chats/${started.threadId}`);
    } catch {
      // ignore
    }
  }

  // ✅ REAL: Pop balloon
  async pop(m: MatchListItem, ev: MouseEvent) {
    ev.stopPropagation();
    if (this.busyId) return;

    this.busyId = m.matchId;
    try {
      await firstValueFrom(this.matchesApi.pop(m.matchId));
      await this.load();
    } catch {
      this.error = 'Could not pop balloon.';
    } finally {
      this.busyId = null;
      this.cdr.markForCheck();
    }
  }

  // ✅ View profile (uses Review-like publicPreview)
  viewProfile(m: MatchListItem, ev: MouseEvent) {
    ev.stopPropagation();
    this.router.navigateByUrl(`/home/matches/${m.matchId}/profile`);
  }

  isBusy(m: MatchListItem): boolean {
    return this.busyId === m.matchId;
  }

  get countText(): string {
    const n = this.matches.length;
    return n === 1 ? '1 balloon' : `${n} balloons`;
  }
}
