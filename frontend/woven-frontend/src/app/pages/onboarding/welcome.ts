import { Component, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

@Component({
  selector: 'woven-onboarding-welcome',
  standalone: true,
  imports: [CommonModule, OnboardingShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <woven-onboarding-shell title="Welcome to Woven." stepLabel="Welcome">

      <div class="welcome">
        <div class="lines">
          <p class="line">Five people a day. Chosen for you. Not for everyone.</p>
          <p class="line">You'll respond with your head, your heart, or hold space — and we'll learn from every choice.</p>
          <p class="line">No endless scrolling. No performance. Just the people most likely to matter.</p>
        </div>

        <div class="divider"></div>

        <div class="expect">
          <div class="expectRow">
            <span class="icon">◈</span>
            <span class="expectText">About 5 minutes to set up your profile</span>
          </div>
          <div class="expectRow">
            <span class="icon">◇</span>
            <span class="expectText">Five thoughtful questions — the heart of how we match</span>
          </div>
          <div class="expectRow">
            <span class="icon">♡</span>
            <span class="expectText">A few photos and a handful of honest answers</span>
          </div>
        </div>

        <div class="divider"></div>

        <p class="note">You can edit everything later. Nothing is permanent except showing up.</p>

        <button class="cta" (click)="next()" [disabled]="loading">
          <span *ngIf="!loading">Let's build your profile →</span>
          <span *ngIf="loading">Just a moment…</span>
        </button>

        <p class="err" *ngIf="err">{{ err }}</p>
      </div>

    </woven-onboarding-shell>
  `,
  styles: [`
    .welcome { display: grid; gap: 0; }

    .lines { display: grid; gap: 14px; margin-bottom: 28px; }

    .line {
      font-family: var(--font-ui);
      font-size: 15px;
      line-height: 1.65;
      color: var(--text-secondary);
    }

    .divider {
      height: 1px;
      background: linear-gradient(90deg, transparent, var(--border-soft), transparent);
      margin: 20px 0;
    }

    .expect { display: grid; gap: 14px; }

    .expectRow {
      display: flex;
      align-items: flex-start;
      gap: 14px;
    }

    .icon {
      font-size: 16px;
      opacity: 0.6;
      flex: 0 0 auto;
      margin-top: 1px;
    }

    .expectText {
      font-family: var(--font-ui);
      font-size: 14px;
      color: var(--text-muted);
      line-height: 1.5;
    }

    .note {
      font-family: var(--font-ui);
      font-size: 12px;
      color: var(--text-dim);
      line-height: 1.55;
      font-style: italic;
    }

    .cta {
      width: 100%;
      margin-top: 8px;
      padding: 16px 24px;
      border: none;
      border-radius: var(--radius-xl);
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base);
      font-family: var(--font-ui);
      font-size: 15px;
      font-weight: 700;
      letter-spacing: 0.01em;
      cursor: pointer;
      transition: opacity 0.2s ease, transform 0.15s ease;
      box-shadow: 0 4px 20px rgba(212, 160, 23, 0.28);
    }

    .cta:hover:not(:disabled) { opacity: 0.88; }
    .cta:active:not(:disabled) { transform: scale(0.96); }
    .cta:disabled { opacity: 0.5; cursor: not-allowed; }

    .err {
      font-size: 12px;
      color: var(--rose-300);
      text-align: center;
      margin-top: 8px;
    }
  `],
})
export class WelcomeOnboardingComponent {
  loading = false;
  err = '';

  constructor(
    private onboarding: OnboardingService,
    private router: Router,
    private cdr: ChangeDetectorRef,
  ) {}

  async next() {
    this.loading = true;
    this.err = '';
    this.cdr.markForCheck();
    try {
      const res = await firstValueFrom(this.onboarding.submitWelcome());
      this.router.navigateByUrl(res.nextRoute || '/onboarding/basics');
    } catch {
      this.err = 'Something went wrong. Please try again.';
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
    }
  }
}
