import { Component, OnInit, OnDestroy, ChangeDetectorRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterOutlet } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { PulseAnswers, PulseService, PulseState } from '../../services/pulse.service';
import { RealtimeService } from '../../services/realtime.service';
import { PushService } from '../../services/push.service';
import { FeedbackService, FeedbackPrompt } from '../../services/feedback.service';
import { CoachingService, CoachingSummary } from '../../services/coaching.service';
import { PulseSheetComponent } from './pulse-sheet.component';
import { HowItWorksSheetComponent } from './how-it-works-sheet.component';
import { WovenAssistantComponent } from '../../components/woven-assistant/woven-assistant.component';
import { CoachingCardComponent } from '../../components/coaching-card/coaching-card.component';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterOutlet, PulseSheetComponent, HowItWorksSheetComponent, WovenAssistantComponent, CoachingCardComponent],
  templateUrl: './home.html',
  styleUrls: ['./home.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent implements OnInit, OnDestroy {
  pulse: PulseState | null = null;
  pulseError = '';
  pulseSheetOpen = false;
  savingPulse = false;
  howItWorksOpen = false;
  countdownText = '';
  private timer: any;

  // Push nudge (one-time banner for notification opt-in)
  showPushNudge = false;

  // Coaching summary card
  coachingSummary: CoachingSummary | null = null;

  // Feedback prompt
  feedbackPrompt: FeedbackPrompt | null = null;
  feedbackMet: boolean | null = null;
  feedbackStars = 0;
  feedbackSubmitting = false;

  constructor(
    public router: Router,
    private pulseApi: PulseService,
    private realtime: RealtimeService,
    private push: PushService,
    private feedbackSvc: FeedbackService,
    private coaching: CoachingService,
    private cdr: ChangeDetectorRef,
  ) {}

  async ngOnInit() {
    this.realtime.start();
    await this.refreshPulse();
    this.startCountdownTicker();
    this.checkPushNudge();
    this.checkFeedbackPrompt();
    this.checkCoachingSummary();
  }

  ngOnDestroy() {
    if (this.timer) clearInterval(this.timer);
    this.realtime.stop();
  }

  go(path: 'moments' | 'commons' | 'chats' | 'you') {
    this.router.navigateByUrl(`/${path}`);
  }

  isActive(prefix: string) {
    return this.router.url === prefix || this.router.url.startsWith(prefix + '/');
  }

  get activeLabel(): string {
    const url = this.router.url;
    if (url.startsWith('/moments')) return 'Moments';
    if (url.startsWith('/commons')) return 'Commons';
    if (url.startsWith('/chats'))   return 'Chats';
    if (url.startsWith('/you'))     return 'You';
    return '';
  }

  openHowItWorks() { this.howItWorksOpen = true;  this.cdr.markForCheck(); }
  closeHowItWorks(){ this.howItWorksOpen = false; this.cdr.markForCheck(); }
  openPulse()      { if (this.pulse) { this.pulseSheetOpen = true;  this.cdr.markForCheck(); } }
  closePulse()     { this.pulseSheetOpen = false; this.cdr.markForCheck(); }

  canEditPulse() {
    if (!this.pulse) return false;
    if (!this.pulse.answered) return true;
    return Date.now() >= new Date(this.pulse.cycleEndUtc).getTime();
  }

  async refreshPulse() {
    this.pulseError = '';
    try {
      const res = await firstValueFrom(this.pulseApi.getCurrent());
      this.pulse = res;
      this.updateCountdown();
      if (!res.answered) this.pulseSheetOpen = true;
    } catch {
      this.pulseError = "Pulse couldn't load.";
    }
    this.cdr.markForCheck();
  }

  startCountdownTicker() {
    this.timer = setInterval(() => { this.updateCountdown(); this.cdr.markForCheck(); }, 15_000);
  }

  updateCountdown() {
    if (!this.pulse) { this.countdownText = ''; return; }
    const ms = Math.max(0, new Date(this.pulse.cycleEndUtc).getTime() - Date.now());
    if (ms <= 0) { this.countdownText = 'Reset now'; return; }
    const totalMin = Math.floor(ms / 60000);
    const d = Math.floor(totalMin / 1440);
    const h = Math.floor((totalMin % 1440) / 60);
    const m = totalMin % 60;
    this.countdownText = `Resets in ${d > 0 ? d + 'd ' : ''}${h}h ${m}m`.trim();
  }

  summaryText() {
    if (!this.pulse?.answered) return 'Quick check-in for better matches';
    const answers = this.pulse.answers;
    const qMap = new Map(this.pulse.questions.map((q: any) => [q.id, q]));
    const pick = (qid: any, key: any) => (qMap.get(qid) as any)?.options?.find((o: any) => o.key === key)?.label ?? key;
    return `${pick('d1_battery', answers.d1_battery)} · ${pick('d2_tone', answers.d2_tone)} · ${pick('d3_role', answers.d3_role)}`;
  }

  toneHint() {
    if (!this.pulse?.answered) return 'Takes ~10 seconds';
    const t = this.pulse.answers?.d2_tone;
    if (t === 'playful') return 'Leans toward lighter conversations';
    if (t === 'serious') return 'Leans toward more meaningful conversations';
    return 'Leans toward relaxed conversations';
  }

  // ── Push nudge ──────────────────────────────────────────────────────────────

  private async checkPushNudge() {
    if (typeof window === 'undefined') return;
    if (localStorage.getItem('woven:pushAsked')) return;
    const supported = await this.push.isSupported();
    if (!supported) return;
    const subscribed = await this.push.isSubscribed();
    if (subscribed) return;
    this.showPushNudge = true;
    this.cdr.markForCheck();
  }

  async enablePush() {
    localStorage.setItem('woven:pushAsked', '1');
    this.showPushNudge = false;
    this.cdr.markForCheck();
    await this.push.register();
  }

  dismissPushNudge() {
    localStorage.setItem('woven:pushAsked', '1');
    this.showPushNudge = false;
    this.cdr.markForCheck();
  }

  // ── Feedback prompt ──────────────────────────────────────────────────────────

  private checkFeedbackPrompt() {
    this.feedbackSvc.getPrompt().subscribe({
      next: (res) => {
        if (res.hasPendingPrompt) {
          this.feedbackPrompt = res;
          this.cdr.markForCheck();
        }
      },
      error: () => {}
    });
  }

  setFeedbackMet(met: boolean) {
    this.feedbackMet = met;
    if (!met) this.feedbackStars = 0;
    this.cdr.markForCheck();
  }

  setFeedbackStars(stars: number) {
    this.feedbackStars = stars;
    this.cdr.markForCheck();
  }

  dismissFeedback() {
    this.feedbackPrompt = null;
    this.cdr.markForCheck();
  }

  async submitFeedback() {
    if (!this.feedbackPrompt?.matchId || this.feedbackSubmitting) return;
    if (this.feedbackMet === null) return;

    this.feedbackSubmitting = true;
    this.cdr.markForCheck();

    this.feedbackSvc.submit(this.feedbackPrompt.matchId, {
      metInPerson: this.feedbackMet,
      stars: this.feedbackMet && this.feedbackStars > 0 ? this.feedbackStars : null,
    }).subscribe({
      next: () => {
        this.feedbackPrompt = null;
        this.feedbackMet = null;
        this.feedbackStars = 0;
        this.feedbackSubmitting = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.feedbackSubmitting = false;
        this.cdr.markForCheck();
      }
    });
  }

  // ── Coaching summary ─────────────────────────────────────────────────────────

  private checkCoachingSummary() {
    this.coaching.getCurrentSummary().subscribe({
      next: (summary) => {
        if (summary) {
          this.coachingSummary = summary;
          this.cdr.markForCheck();
        }
      },
      error: () => { /* 204 or error — no card */ }
    });
  }

  onCoachingDismissed() {
    this.coachingSummary = null;
    this.cdr.markForCheck();
  }

  async submitPulse(answers: PulseAnswers) {
    if (!this.pulse || !this.canEditPulse()) { this.closePulse(); return; }
    this.savingPulse = true;
    this.pulseError = '';
    this.cdr.markForCheck();
    try {
      await firstValueFrom(this.pulseApi.submit(answers));
      await this.refreshPulse();
      this.closePulse();
    } catch {
      this.pulseError = 'Could not save Pulse. Try again.';
    } finally {
      this.savingPulse = false;
      this.cdr.markForCheck();
    }
  }
}
