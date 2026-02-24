import { Component, OnInit, OnDestroy, ChangeDetectorRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterOutlet } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { PulseAnswers, PulseService, PulseState } from '../../services/pulse.service';
import { PulseSheetComponent } from './pulse-sheet.component';
import { HowItWorksSheetComponent } from './how-it-works-sheet.component';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterOutlet, PulseSheetComponent, HowItWorksSheetComponent],
  templateUrl: './home.html',
  styleUrls: ['./home.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent implements OnInit, OnDestroy {
  pulse: PulseState | null = null;
  pulseError = '';
  pulseSheetOpen = false;
  savingPulse = false;

  // ✅ NEW: How It Works sheet
  howItWorksOpen = false;

  countdownText = '';
  private timer: any;

  constructor(
    public router: Router,
    private pulseApi: PulseService,
    private cdr: ChangeDetectorRef
  ) {}

  async ngOnInit() {
    await this.refreshPulse();
    this.startCountdownTicker();
  }

  ngOnDestroy() {
    if (this.timer) clearInterval(this.timer);
  }

  go(path: 'moments' | 'chats' | 'profile') {
    this.router.navigateByUrl(`/home/${path}`);
  }

  isActive(prefix: string) {
    return this.router.url.startsWith(prefix);
  }

  // ✅ NEW: Toggle How It Works
  openHowItWorks() {
    console.log('🔵 Info button clicked!');
    this.howItWorksOpen = true;
    this.cdr.detectChanges(); // ✅ Force change detection
    console.log('🔵 howItWorksOpen is now:', this.howItWorksOpen);
  }

  closeHowItWorks() {
    console.log('🔵 Closing How It Works sheet');
    this.howItWorksOpen = false;
    this.cdr.detectChanges(); // ✅ Force change detection
  }

  async refreshPulse() {
    this.pulseError = '';
    try {
      const res = await firstValueFrom(this.pulseApi.getCurrent());
      this.pulse = res;

      this.updateCountdown();

      // auto-open only when unanswered
      if (!res.answered) this.pulseSheetOpen = true;
    } catch {
      this.pulseError = "Pulse couldn't load.";
    }
  }

  startCountdownTicker() {
    this.timer = setInterval(() => this.updateCountdown(), 15_000);
  }

  updateCountdown() {
    if (!this.pulse) { this.countdownText = ''; return; }

    const end = new Date(this.pulse.cycleEndUtc).getTime();
    const now = Date.now();
    const ms = Math.max(0, end - now);

    const totalMinutes = Math.floor(ms / 60000);
    const days = Math.floor(totalMinutes / (60 * 24));
    const hours = Math.floor((totalMinutes % (60 * 24)) / 60);
    const mins = totalMinutes % 60;

    if (ms <= 0) {
      this.countdownText = 'Reset now';
      return;
    }

    const d = days > 0 ? `${days}d ` : '';
    const h = `${hours}h`;
    const m = mins >= 0 ? ` ${mins}m` : '';
    this.countdownText = `Resets in ${d}${h}${m}`.trim();
  }

  openPulse() {
    if (!this.pulse) return;
    this.pulseSheetOpen = true;
  }

  closePulse() {
    this.pulseSheetOpen = false;
  }

  canEditPulse() {
    if (!this.pulse) return false;
    if (!this.pulse.answered) return true;

    // allow edits only after cycle end
    const end = new Date(this.pulse.cycleEndUtc).getTime();
    return Date.now() >= end;
  }

  summaryText() {
    if (!this.pulse?.answered) return 'Quick check-in for better matches';

    const answers = this.pulse.answers;
    const qMap = new Map(this.pulse.questions.map(q => [q.id, q]));
    const pick = (qid: any, key: any) => qMap.get(qid)?.options?.find(o => o.key === key)?.label ?? key;

    const battery = pick('d1_battery', answers.d1_battery);
    const tone = pick('d2_tone', answers.d2_tone);
    const role = pick('d3_role', answers.d3_role);

    return `${battery} • ${tone} • ${role}`;
  }

  toneHint() {
    if (!this.pulse?.answered) return 'Takes ~10 seconds';

    const t = this.pulse.answers?.d2_tone;
    if (t === 'playful') return 'Leans toward lighter conversations';
    if (t === 'serious') return 'Leans toward more meaningful conversations';
    return 'Leans toward relaxed conversations';
  }

  async submitPulse(answers: PulseAnswers) {
    if (!this.pulse) return;

    if (!this.canEditPulse()) {
      this.closePulse();
      return;
    }

    this.savingPulse = true;
    this.pulseError = '';

    try {
      await firstValueFrom(this.pulseApi.submit(answers));
      await this.refreshPulse();
      this.closePulse();
    } catch {
      this.pulseError = 'Could not save Pulse. Try again.';
    } finally {
      this.savingPulse = false;
    }
  }
}