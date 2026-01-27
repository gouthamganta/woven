import {
  Component,
  OnInit,
  AfterViewChecked,
  OnDestroy,
  HostListener,
  ChangeDetectorRef,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { ActivatedRoute, Router, NavigationEnd } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, Subscription, filter } from 'rxjs';
import { ChatService, ChatThreadResponse } from '../../services/chat.service';
import { MatchesService } from '../../services/matches.service';
import { GamesService, FinalResultResponse } from '../../services/games.service';
import { GameMessageCardComponent } from '../../components/game-message-card/game-message-card.component';
import { InlineGamePlayerComponent } from '../../components/inline-game-player/inline-game-player.component';
import { TrialDecisionComponent } from '../../components/trial-decision/trial-decision.component';
import { UnmatchRatingComponent } from '../../components/unmatch-rating/unmatch-rating.component';

@Component({
  selector: 'app-chat-thread',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    DatePipe,
    GameMessageCardComponent,
    InlineGamePlayerComponent,
    TrialDecisionComponent,
    UnmatchRatingComponent
  ],
  templateUrl: './chat-thread.component.html',
  styleUrls: ['./chat-thread.component.scss'],
})
export class ChatThreadComponent implements OnInit, AfterViewChecked, OnDestroy {
  loading = true;
  error = '';
  data: ChatThreadResponse | null = null;

  body = '';
  toast = '';

  now = Date.now();
  private timer?: number;

  private busy = false;
  sending = false;

  showMore = false;
  private readonly dateIdeaDelayMs = 10 * 60 * 1000;
  private shouldAutoScroll = false;
  private navSub?: Subscription;
  private currentThreadId: string | null = null;
  private loadingThreadId: string | null = null;

  isInitialLoad = true;
  isBalloonStage = false;
  isFindLoveStage = false;
  isPopping = false;
  isUnmatching = false;
  isBlocking = false;
  justTransitionedToFindLove = false;
  dateIdeaJustUnlocked = false;
  private previousFindLoveState = false;

  showGamePicker = false;

  // Inline game player state
  showGamePlayer = false;
  currentGameSessionId: string | null = null;
  currentGameType: string = 'GAME';

  // Trial decision state
  showTrialDecision = false;
  isInTrial = false;
  trialSecondsLeft = 0;

  // Unmatch rating state
  showUnmatchRating = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private chatApi: ChatService,
    private matchesApi: MatchesService,
    private games: GamesService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    if (typeof window !== 'undefined') {
      this.timer = window.setInterval(() => {
        const wasFindLove = this.isFindLoveReady();
        this.now = Date.now();
        const nowFindLove = this.isFindLoveReady();

        if (!wasFindLove && nowFindLove) {
          this.triggerFindLoveTransition();
        }

        const wasRevealed = this.previousFindLoveState;
        const nowRevealed = this.shouldRevealDateIdea();
        if (!wasRevealed && nowRevealed) {
          this.triggerDateIdeaUnlock();
        }
        this.previousFindLoveState = nowRevealed;

        this.cdr.detectChanges();
      }, 1000);
    }

    this.tryLoadFromRouteTree();

    this.navSub = this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => {
        this.tryLoadFromRouteTree();
      });
  }

  ngAfterViewChecked() {
    if (this.shouldAutoScroll) {
      this.shouldAutoScroll = false;
      setTimeout(() => this.scrollToBottom(), 0);
    }
  }

  ngOnDestroy() {
    if (this.timer && typeof window !== 'undefined') window.clearInterval(this.timer);
    this.navSub?.unsubscribe();
  }

  private getThreadIdFromRouteTree(): string | null {
    let snap: any = this.route.snapshot;

    while (snap?.parent) snap = snap.parent;

    const stack: any[] = [snap];
    while (stack.length) {
      const s = stack.pop();
      const id = s?.paramMap?.get?.('threadId');
      if (id) return id;

      const children = s?.children ?? [];
      for (const c of children) stack.push(c);
    }
    return null;
  }

  private tryLoadFromRouteTree() {
    const id = this.getThreadIdFromRouteTree();

    if (!id) {
      this.loading = false;
      this.error = 'Missing thread id';
      this.cdr.detectChanges();
      return;
    }

    if (this.currentThreadId === id) return;
    if (this.loadingThreadId === id) return;

    this.loadingThreadId = id;

    this.load(id, { silent: false })
      .then(() => {
        this.currentThreadId = id;
      })
      .finally(() => {
        this.loadingThreadId = null;
      });
  }

  private storageKeyForIdea(threadId: string) {
    return `woven:dateIdeaUnlockAt:${threadId}`;
  }

  private markDateIdeaUnlockStart(threadId: string) {
    if (typeof window === 'undefined') return;
    const key = this.storageKeyForIdea(threadId);
    const existing = window.localStorage.getItem(key);
    if (existing) return;
    window.localStorage.setItem(key, String(this.now));
  }

  private getIdeaUnlockStart(threadId: string): number | null {
    if (typeof window === 'undefined') return null;
    const raw = window.localStorage.getItem(this.storageKeyForIdea(threadId));
    if (!raw) return null;
    const n = Number(raw);
    return Number.isFinite(n) ? n : null;
  }

  shouldShowDateIdeaBox(): boolean {
    return !!this.data && this.isFindLoveReady();
  }

  shouldRevealDateIdea(): boolean {
    const d = this.data;
    if (!d) return false;
    if (!this.isFindLoveReady()) return false;

    this.markDateIdeaUnlockStart(d.threadId);
    const startedAt = this.getIdeaUnlockStart(d.threadId);
    if (!startedAt) return false;

    return this.now - startedAt >= this.dateIdeaDelayMs;
  }

  dateIdeaCountdown(): string {
    const d = this.data;
    if (!d) return '';
    this.markDateIdeaUnlockStart(d.threadId);

    const startedAt = this.getIdeaUnlockStart(d.threadId) ?? this.now;
    const left = Math.max(0, this.dateIdeaDelayMs - (this.now - startedAt));
    const s = Math.ceil(left / 1000);
    const mm = Math.floor(s / 60);
    const ss = s % 60;
    return `${mm.toString().padStart(2, '0')}:${ss.toString().padStart(2, '0')}`;
  }

  isCountdownUrgent(): boolean {
    const iso = this.data?.findLoveAt;
    if (!iso) return false;
    const ms = new Date(iso).getTime() - this.now;
    return ms > 0 && ms < 2 * 60 * 1000;
  }

  async load(threadId: string, opts?: { silent?: boolean }) {
    const silent = !!opts?.silent;

    if (!silent) {
      this.loading = true;
      this.error = '';
      this.cdr.detectChanges();
    }

    try {
      const fresh = await firstValueFrom(this.chatApi.thread(threadId));

      this.data = {
        ...fresh,
        messages: [...(fresh.messages ?? [])],
      };

      if (this.data) {
        const isFL = this.isFindLoveReady();
        this.isBalloonStage = !isFL && !this.data.isTrial;
        this.isFindLoveStage = isFL;

        if (isFL) {
          this.markDateIdeaUnlockStart(this.data.threadId);
        }
      }

      if (this.isInitialLoad && this.isBalloonStage) {
        this.isInitialLoad = false;
      }

      // Check trial status
      this.checkTrialStatus();

      this.shouldAutoScroll = true;
      this.cdr.detectChanges();
    } catch (err: any) {
      console.error('❌ Chat load error:', err);
      console.error('❌ Error details:', {
        status: err?.status,
        statusText: err?.statusText,
        message: err?.message,
        error: err?.error
      });
      this.error = 'Could not load chat.';
      this.cdr.detectChanges();
    } finally {
      this.loading = false;
      this.cdr.detectChanges();
    }
  }

  async refreshSilent() {
    const id = this.data?.threadId ?? this.getThreadIdFromRouteTree();
    if (!id) return;
    await this.load(id, { silent: true });
  }

  private triggerFindLoveTransition() {
    this.justTransitionedToFindLove = true;
    this.isBalloonStage = false;
    this.isFindLoveStage = true;
    this.cdr.detectChanges();

    setTimeout(() => {
      this.justTransitionedToFindLove = false;
      this.cdr.detectChanges();
    }, 2000);
  }

  private triggerDateIdeaUnlock() {
    this.dateIdeaJustUnlocked = true;
    this.cdr.detectChanges();

    setTimeout(() => {
      this.dateIdeaJustUnlocked = false;
      this.cdr.detectChanges();
    }, 2000);
  }

  back() {
    this.router.navigateByUrl('/home/chats');
  }

  titleName(): string {
    return this.data?.other?.fullName ?? 'Chat';
  }

  countdownSafe(iso: string | null | undefined): string {
    if (!iso) return '00:00';
    return this.countdown(iso);
  }

  isFindLoveReady(): boolean {
    const iso = this.data?.findLoveAt;
    if (!iso) return false;
    return new Date(iso).getTime() <= this.now;
  }

  statusText(): string {
    // Show trial timer if in trial
    if (this.isInTrial && this.trialSecondsLeft > 0) {
      const mm = Math.floor(this.trialSecondsLeft / 60);
      const ss = this.trialSecondsLeft % 60;
      return `Trial · ${mm.toString().padStart(2, '0')}:${ss.toString().padStart(2, '0')}`;
    }

    if (this.isInTrial && this.trialSecondsLeft <= 0) {
      return 'Trial Ended';
    }

    const iso = this.data?.findLoveAt;
    if (!iso) return 'Balloon';

    const t = new Date(iso).getTime();
    if (t > this.now) return `Balloon · ${this.countdown(iso)}`;
    return 'Find Love';
  }

  countdown(iso: string): string {
    const ms = new Date(iso).getTime() - this.now;
    if (ms <= 0) return '00:00';
    const s = Math.floor(ms / 1000);
    const mm = Math.floor(s / 60);
    const ss = s % 60;
    return `${mm.toString().padStart(2, '0')}:${ss.toString().padStart(2, '0')}`;
  }

  checkTrialStatus() {
    if (!this.data) return;

    this.isInTrial = !!this.data.isTrial;
    this.trialSecondsLeft = this.data.trialSecondsLeft ?? 0;

    // ✅ Only show modal when trial has ended AND user hasn't decided yet
    if (this.data.canMakeDecision && !this.showTrialDecision) {
      const myDecision = this.data.isUserA ? this.data.userADecision : this.data.userBDecision;

      // Only show if user hasn't decided yet
      if (!myDecision) {
        this.showTrialDecision = true;
      }
    }
  }

  formatTrialTime(seconds: number): string {
    const mm = Math.floor(seconds / 60);
    const ss = seconds % 60;
    return `${mm}:${ss.toString().padStart(2, '0')}`;
  }

  getTrialProgress(): number {
    // Assuming trial is 60 seconds (1 minute)
    const totalTrialSeconds = 60;
    return Math.max(0, Math.min(100, ((totalTrialSeconds - this.trialSecondsLeft) / totalTrialSeconds) * 100));
  }

  async onTrialDecision(event: { decision: 'CONTINUE' | 'END'; rating?: number }) {
    if (!this.data) return;

    try {
      const result = await firstValueFrom(
        this.chatApi.trialDecision(this.data.threadId, event.decision, event.rating)
      );

      this.showTrialDecision = false;

      if (result.status === 'MATCH_ENDED') {
        this.showToast('Match ended.');
        setTimeout(() => {
          this.router.navigateByUrl('/home/chats');
        }, 800);
      } else if (result.status === 'MATCH_CONTINUES') {
        this.showToast('Match continues!');
        await this.refreshSilent();
      } else {
        this.showToast('Decision recorded. Waiting for them...');
        await this.refreshSilent();
      }
    } catch (err) {
      console.error('Trial decision error:', err);
      this.showToast('Could not submit decision.');
      this.showTrialDecision = false;
    }
  }

  isMine(senderUserId: number): boolean {
    const otherId = this.data?.other?.userId;
    if (!otherId) return false;
    return senderUserId !== otherId;
  }

  isSystem(msg: any): boolean {
    if (!msg) return false;
    const msgType = (msg?.messageType || '').toUpperCase();
    return msgType === 'SYSTEM';
  }

  isGame(msg: any): boolean {
    if (!msg) return false;

    const msgType = (msg?.messageType || '').toUpperCase();
    if (msgType === 'GAME') return true;

    if (msg.meta && typeof msg.meta === 'object') {
      const hasSessionId = 'sessionId' in msg.meta;
      const hasGameType = 'gameType' in msg.meta;
      if (hasSessionId && hasGameType) return true;
    }

    if (msg.body && typeof msg.body === 'string' && msg.body.startsWith('🎮')) {
      return true;
    }

    return false;
  }

  onAcceptGame(sessionId: string) {
    console.log('🎮 Accepting game:', sessionId);
    this.games.acceptSession(sessionId).subscribe({
      next: () => {
        this.showToast('Game accepted!');
        setTimeout(() => {
          console.log('🔄 Refreshing chat to get updated game status...');
          this.refreshSilent();
        }, 500);
      },
      error: (err) => {
        console.error('❌ Accept game error:', err);
        this.showToast('Cannot accept game');
      },
    });
  }

  onRejectGame(sessionId: string) {
    console.log('🎮 Rejecting game:', sessionId);
    this.games.rejectSession(sessionId).subscribe({
      next: () => {
        this.showToast('Game declined');
        this.refreshSilent();
      },
      error: (err) => {
        console.error('❌ Reject game error:', err);
        this.showToast('Cannot reject game');
      },
    });
  }

  onOpenGame(sessionId: string) {
    console.log('🎮 Opening game result:', sessionId);
    this.games.getResult(sessionId).subscribe({
      next: (r: FinalResultResponse) => {
        alert(`${r.gameType}\nYou: ${r.yourScore} | Them: ${r.theirScore}\n\n${r.aiInsight}`);
      },
      error: (err) => {
        console.error('❌ Get result error:', err);
        this.showToast('Result not available yet');
      },
    });
  }

  async send() {
    if (!this.data) return;
    if (this.sending) return;

    const text = (this.body || '').trim();
    if (!text) return;

    this.sending = true;

    const tempId = `tmp_${Date.now()}`;
    const myId = this.data.meUserId ?? 0;
    const threadId = this.data.threadId;

    const optimisticMsg = {
      messageId: tempId,
      senderUserId: myId,
      body: text,
      messageType: 'CHAT' as const,
      createdAt: new Date().toISOString(),
    };

    this.data = {
      ...this.data,
      messages: [...(this.data.messages ?? []), optimisticMsg as any],
    };
    this.body = '';

    this.shouldAutoScroll = true;
    this.cdr.detectChanges();

    try {
      await firstValueFrom(this.chatApi.send(threadId, text));
      await new Promise((r) => setTimeout(r, 150));
      await this.load(threadId, { silent: true });
    } catch (err) {
      console.error('❌ Send error:', err);
      if (this.data) {
        this.data = {
          ...this.data,
          messages: (this.data.messages ?? []).filter((m) => m.messageId !== tempId),
        };
      }
      this.showToast('Could not send.');
      this.cdr.detectChanges();
    } finally {
      this.sending = false;
      this.cdr.detectChanges();
    }
  }

  viewProfile() {
    const matchId = (this.data as any)?.matchId;
    if (!matchId) return;
    this.router.navigateByUrl(`/home/matches/${matchId}/profile`);
  }

  async popBalloon() {
    if (!this.data || this.busy) return;
    const matchId = (this.data as any)?.matchId;
    if (!matchId) return;

    this.isPopping = true;
    this.busy = true;
    this.cdr.detectChanges();

    await new Promise((r) => setTimeout(r, 600));

    try {
      const result = await firstValueFrom(this.matchesApi.pop(matchId));
      
      // ✅ Pop now starts trial, not closes match
      if (result.status === 'TRIAL_STARTED') {
        this.showToast('Trial period started! 1 minute ⏱️');
        // Refresh to get updated trial state
        await this.refreshSilent();
      } else {
        this.showToast('Balloon popped.');
        this.router.navigateByUrl('/home/chats');
      }
    } catch (err) {
      console.error('Pop error:', err);
      this.showToast('Could not pop.');
    } finally {
      this.isPopping = false;
      this.busy = false;
      this.cdr.detectChanges();
    }
  }

  async unmatch() {
    if (!this.data || this.busy) return;
    
    // ✅ Show rating modal first
    this.showUnmatchRating = true;
    this.closeMore();
  }

  async confirmUnmatch(rating: number | undefined) {
    this.showUnmatchRating = false;
    
    if (!this.data || this.busy) return;
    const matchId = (this.data as any)?.matchId;
    if (!matchId) return;

    this.isUnmatching = true;
    this.busy = true;
    this.cdr.detectChanges();

    await new Promise((r) => setTimeout(r, 500));

    try {
      await firstValueFrom(this.matchesApi.unmatch(matchId, rating));
      this.showToast('Unmatched.');
      setTimeout(() => {
        this.router.navigateByUrl('/home/chats');
      }, 800);
    } catch {
      this.showToast('Could not unmatch.');
      this.isUnmatching = false;
    } finally {
      this.busy = false;
      this.cdr.detectChanges();
    }
  }

  cancelUnmatch() {
    this.showUnmatchRating = false;
  }

  async block() {
    if (!this.data || this.busy) return;
    const matchId = (this.data as any)?.matchId;
    if (!matchId) return;

    this.isBlocking = true;
    this.busy = true;
    this.cdr.detectChanges();

    await new Promise((r) => setTimeout(r, 400));

    try {
      await firstValueFrom(this.matchesApi.block(matchId));
      this.showToast('Blocked.');
      setTimeout(() => {
        this.router.navigateByUrl('/home/chats');
      }, 800);
    } catch {
      this.showToast('Could not block.');
      this.isBlocking = false;
    } finally {
      this.busy = false;
      this.closeMore();
      this.cdr.detectChanges();
    }
  }

  toggleMore(ev?: MouseEvent) {
    ev?.stopPropagation();
    this.showMore = !this.showMore;
    this.cdr.detectChanges();
  }

  closeMore() {
    this.showMore = false;
    this.cdr.detectChanges();
  }

  toggleGamePicker() {
    this.showGamePicker = !this.showGamePicker;
    this.cdr.detectChanges();
  }

  openGamePlayer(event: any) {
    const payload =
      event?.detail?.sessionId ? event.detail :
      event?.sessionId ? event :
      null;

    if (!payload?.sessionId) {
      console.warn('⚠️ openGamePlayer called with invalid event:', event);
      return;
    }

    console.log('🎮 Opening inline game player:', payload.sessionId, payload.gameType);
    this.currentGameSessionId = payload.sessionId;
    this.currentGameType = payload.gameType ?? 'GAME';
    this.showGamePlayer = true;
    this.cdr.detectChanges();
  }

  closeGamePlayer() {
    console.log('🎮 Closing inline game player');
    this.showGamePlayer = false;
    this.currentGameSessionId = null;
    this.cdr.detectChanges();
  }

  onGameCompleted() {
    console.log('🎮 Game completed!');
    this.showToast('Game completed!');
    this.refreshSilent();
  }

  async createGame(gameType: 'KNOW_ME' | 'RED_GREEN_FLAG') {
    if (!this.data) return;
    const matchId = (this.data as any)?.matchId;
    if (!matchId) return;

    console.log('🎮 Creating game:', gameType, 'for match:', matchId);

    this.showGamePicker = false;
    this.cdr.detectChanges();

    try {
      await firstValueFrom(this.games.createSession(matchId, gameType));
      this.showToast('Game request sent!');
      await this.refreshSilent();
    } catch (err) {
      console.error('❌ Create game error:', err);
      this.showToast('Could not create game');
    }
  }

  @HostListener('document:click')
  onDocClick() {
    if (this.showMore) {
      this.showMore = false;
      this.cdr.detectChanges();
    }
  }

  private showToast(msg: string) {
    this.toast = msg;
    this.cdr.detectChanges();
    if (typeof window === 'undefined') return;
    window.setTimeout(() => {
      this.toast = '';
      this.cdr.detectChanges();
    }, 1400);
  }

  private scrollToBottom() {
    if (typeof document === 'undefined') return;
    const el = document.getElementById('msgsEnd');
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }
}