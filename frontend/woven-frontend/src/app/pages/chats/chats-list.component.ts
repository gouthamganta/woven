import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom, Subscription } from 'rxjs';
import { ChatService, ChatListItem } from '../../services/chat.service';
import { MatchesService } from '../../services/matches.service';
import { RealtimeService, NewChatMessageEvent } from '../../services/realtime.service';

@Component({
  selector: 'app-chats-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './chats-list.component.html',
  styleUrls: ['./chats-list.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatsListComponent implements OnInit, OnDestroy {
  loading = true;
  error = '';
  chats: ChatListItem[] = [];
  unreadThreadIds = new Set<string>();
  meUserId = 0;

  now = Date.now();
  private timer?: number;
  private busyId: string | null = null;
  private rtSub?: Subscription;

  constructor(
    private chatApi: ChatService,
    private matchesApi: MatchesService,
    private router: Router,
    private cdr: ChangeDetectorRef,
    private realtime: RealtimeService
  ) {}

  async ngOnInit() {
    await this.load();

    if (typeof window !== 'undefined') {
      this.timer = window.setInterval(() => { this.now = Date.now(); this.cdr.markForCheck(); }, 1000);
    }

    this.rtSub = this.realtime.newChatMessage$.subscribe((e: NewChatMessageEvent) => {
      const idx = this.chats.findIndex((c) => c.threadId === e.threadId);
      if (idx === -1) return;
      const updated: ChatListItem = {
        ...this.chats[idx],
        lastMessage: { body: e.body, createdAt: e.createdAt, senderUserId: e.senderUserId },
        updatedAt: e.createdAt,
      };
      this.chats = [updated, ...this.chats.filter((_, i) => i !== idx)];
      this.unreadThreadIds.add(e.threadId);
      this.cdr.markForCheck();
    });
  }

  ngOnDestroy() {
    if (this.timer && typeof window !== 'undefined') window.clearInterval(this.timer);
    this.rtSub?.unsubscribe();
  }

  async load() {
    this.loading = true;
    this.error = '';
    try {
      const res = await firstValueFrom(this.chatApi.list());
      this.chats = res?.chats ?? [];
      this.meUserId = res?.meUserId ?? 0;
    } catch {
      this.error = 'Could not load balloons.';
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
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
    this.unreadThreadIds.delete(c.threadId);
    this.router.navigateByUrl(`/chats/${c.threadId}`);
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
      this.cdr.markForCheck();
    }
  }

  viewProfile(c: ChatListItem, ev: MouseEvent) {
    ev.stopPropagation();
    const matchId = (c as any)?.matchId;
    if (!matchId) return;
    this.router.navigateByUrl(`/matches/${matchId}/profile`);
  }

  isOnline(c: ChatListItem): boolean {
    if (!c.other?.lastActiveAt) return false;
    return (this.now - new Date(c.other.lastActiveAt).getTime()) < 5 * 60 * 1000;
  }

  lastSeenLabel(c: ChatListItem): string | null {
    if (!c.other?.lastActiveAt) return null;
    const ms = this.now - new Date(c.other.lastActiveAt).getTime();
    if (ms < 5 * 60 * 1000) return 'Online now';
    const mins = Math.floor(ms / 60_000);
    if (mins < 60) return `Active ${mins}m ago`;
    const hrs = Math.floor(ms / 3_600_000);
    if (hrs < 24) return `Active ${hrs}h ago`;
    return null;
  }

  isMyTurn(c: ChatListItem): boolean {
    if (!c.lastMessage) return false;
    return c.lastMessage.senderUserId !== this.meUserId;
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
