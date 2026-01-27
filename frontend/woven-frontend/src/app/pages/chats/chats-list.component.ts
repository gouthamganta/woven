import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ChatService, ChatListItem } from '../../services/chat.service';
import { MatchesService } from '../../services/matches.service';

@Component({
  selector: 'app-chats-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './chats-list.component.html',
  styleUrls: ['./chats-list.component.scss'],
})
export class ChatsListComponent implements OnInit, OnDestroy {
  loading = true;
  error = '';
  chats: ChatListItem[] = [];

  now = Date.now();
  private timer?: number;
  private busyId: string | null = null;

  constructor(
    private chatApi: ChatService,
    private matchesApi: MatchesService,
    private router: Router
  ) {}

  async ngOnInit() {
    await this.load();

    // ✅ SSR-safe
    if (typeof window !== 'undefined') {
      this.timer = window.setInterval(() => (this.now = Date.now()), 1000);
    }
  }

  ngOnDestroy() {
    if (this.timer && typeof window !== 'undefined') window.clearInterval(this.timer);
  }

  async load() {
    this.loading = true;
    this.error = '';
    try {
      const res = await firstValueFrom(this.chatApi.list());
      this.chats = res?.chats ?? [];
    } catch {
      this.error = 'Could not load balloons.';
    } finally {
      this.loading = false;
    }
  }

  headline(c: ChatListItem): string {
    return c.other?.fullName ?? 'Someone';
  }

  statusLine(c: ChatListItem): string {
    // Show trial status
    if (c.isTrial && (c.trialSecondsLeft ?? 0) > 0) {
      const mm = Math.floor((c.trialSecondsLeft ?? 0) / 60);
      const ss = (c.trialSecondsLeft ?? 0) % 60;
      return `Trial Mode · ${mm.toString().padStart(2, '0')}:${ss.toString().padStart(2, '0')} left`;
    }
    if (c.isTrial) {
      return 'Trial ended · Make your decision';
    }

    if (!c.findLoveAt) return 'Balloon is warming up…';

    const t = new Date(c.findLoveAt).getTime();
    if (t > this.now) return `Find Love opens in ${this.countdown(c.findLoveAt)}`;
    return 'Find Love is open 💗';
  }

  findLoveChip(c: ChatListItem): string {
    if (c.isTrial) return 'Trial';
    if (!c.findLoveAt) return 'Balloon';
    const t = new Date(c.findLoveAt).getTime();
    if (t > this.now) return `Opens ${this.countdown(c.findLoveAt)}`;
    return 'Find Love';
  }

  isFindLoveReady(c: ChatListItem): boolean {
    if (!c.findLoveAt) return false;
    return new Date(c.findLoveAt).getTime() <= this.now;
  }

  // Pop allowed only if no trial and no Find Love started
  canPop(c: ChatListItem): boolean {
    return !c.findLoveAt && !c.isTrial;
  }

  countdown(iso: string): string {
    const ms = new Date(iso).getTime() - this.now;
    if (ms <= 0) return '00:00';
    const s = Math.floor(ms / 1000);
    const mm = Math.floor(s / 60);
    const ss = s % 60;
    return `${mm.toString().padStart(2, '0')}:${ss.toString().padStart(2, '0')}`;
  }

  open(c: ChatListItem) {
    this.router.navigateByUrl(`/home/chats/${c.threadId}`);
  }

  async pop(c: ChatListItem, ev: MouseEvent) {
    ev.stopPropagation();
    if (this.busyId) return;

    // ✅ guard: if Find Love exists, do nothing (UI should already hide, but double safety)
    if (c.findLoveAt) return;

    const matchId = (c as any)?.matchId;
    if (!matchId) return;

    this.busyId = matchId;
    try {
      await firstValueFrom(this.matchesApi.pop(matchId));
      await this.load();
    } catch {
      this.error = 'Could not pop balloon.';
    } finally {
      this.busyId = null;
    }
  }

  viewProfile(c: ChatListItem, ev: MouseEvent) {
    ev.stopPropagation();
    const matchId = (c as any)?.matchId;
    if (!matchId) return;
    this.router.navigateByUrl(`/home/matches/${matchId}/profile`);
  }

  isBusy(c: ChatListItem): boolean {
    const matchId = (c as any)?.matchId;
    return !!matchId && this.busyId === matchId;
  }

  get countText(): string {
    const n = this.chats.length;
    return n === 1 ? '1 balloon' : `${n} balloons`;
  }
}
