import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, Inject, PLATFORM_ID } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'woven-onboarding-start',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container">
      <div class="content" [class.ready]="ready">

        <!-- Logo/Symbol -->
        <div class="symbol" *ngIf="!ready">◈</div>
        <div class="checkmark" *ngIf="ready">✓</div>

        <!-- Headline -->
        <h1 class="headline" *ngIf="!ready">Building your deck...</h1>
        <h1 class="headline ready" *ngIf="ready">You're all set</h1>

        <!-- Description -->
        <p class="description" *ngIf="!ready">
          ECHO is analyzing your responses and finding your first matches.
        </p>
        <p class="description" *ngIf="ready">
          Your first five Moments are waiting.
        </p>

        <!-- Progress/Status -->
        <div class="status" *ngIf="!ready">
          <div class="dots">
            <span class="dot"></span>
            <span class="dot"></span>
            <span class="dot"></span>
          </div>
          <span class="statusText">{{ statusText }}</span>
        </div>

        <!-- CTA Button -->
        <button class="cta" *ngIf="ready" (click)="enter()">
          Enter Woven
        </button>

        <p class="err" *ngIf="err">{{ err }}</p>
      </div>
    </div>
  `,
  styles: [`
    .container {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 32px;
      background: linear-gradient(180deg, #0A0612 0%, #120A1A 50%, #0A0612 100%);
    }

    .content {
      max-width: 480px;
      width: 100%;
      text-align: center;
      padding: 60px 40px;
      background: rgba(26, 15, 30, 0.6);
      border: 1px solid rgba(212, 160, 23, 0.15);
      border-radius: 24px;
      backdrop-filter: blur(20px);
      transition: all 0.5s ease;
    }

    .content.ready {
      border-color: rgba(212, 160, 23, 0.4);
      box-shadow: 0 8px 32px rgba(212, 160, 23, 0.2);
    }

    .symbol {
      font-size: 72px;
      color: var(--gold-400);
      margin-bottom: 32px;
      animation: float 3s ease-in-out infinite;
      opacity: 0.9;
    }

    .checkmark {
      width: 80px;
      height: 80px;
      margin: 0 auto 32px;
      border-radius: 50%;
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 48px;
      color: var(--bg-base);
      font-weight: bold;
      animation: scaleIn 0.5s ease;
    }

    @keyframes float {
      0%, 100% { transform: translateY(0px); }
      50% { transform: translateY(-10px); }
    }

    @keyframes scaleIn {
      from { transform: scale(0); opacity: 0; }
      to { transform: scale(1); opacity: 1; }
    }

    .headline {
      font-family: var(--font-display);
      font-size: clamp(32px, 5vw, 42px);
      font-weight: 400;
      letter-spacing: -0.02em;
      margin: 0 0 20px;
      color: var(--text-primary);
    }

    .headline.ready {
      background: linear-gradient(135deg, var(--gold-300), var(--text-primary));
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
      animation: fadeIn 0.5s ease;
    }

    .description {
      font-family: var(--font-ui);
      font-size: 16px;
      line-height: 1.6;
      color: var(--text-secondary);
      margin: 0 0 48px;
      max-width: 380px;
      margin-left: auto;
      margin-right: auto;
    }

    .status {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
      margin-bottom: 32px;
    }

    .dots {
      display: flex;
      gap: 10px;
    }

    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--gold-400);
      animation: pulse 1.4s ease-in-out infinite;
    }

    .dot:nth-child(2) { animation-delay: 0.2s; }
    .dot:nth-child(3) { animation-delay: 0.4s; }

    @keyframes pulse {
      0%, 100% { opacity: 0.3; transform: scale(0.8); }
      50% { opacity: 1; transform: scale(1); }
    }

    .statusText {
      font-family: var(--font-ui);
      font-size: 13px;
      font-weight: 500;
      color: var(--text-muted);
      letter-spacing: 0.02em;
    }

    .cta {
      width: 100%;
      max-width: 280px;
      padding: 18px 32px;
      border: none;
      border-radius: 12px;
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base);
      font-family: var(--font-ui);
      font-size: 16px;
      font-weight: 700;
      letter-spacing: 0.01em;
      cursor: pointer;
      transition: all 0.2s ease;
      box-shadow: 0 4px 20px rgba(212, 160, 23, 0.3);
      animation: fadeIn 0.5s ease;
    }

    .cta:hover {
      transform: translateY(-2px);
      box-shadow: 0 6px 28px rgba(212, 160, 23, 0.4);
    }

    .cta:active {
      transform: translateY(0);
    }

    @keyframes fadeIn {
      from { opacity: 0; transform: translateY(10px); }
      to { opacity: 1; transform: translateY(0); }
    }

    .err {
      font-size: 13px;
      color: var(--rose-300);
      margin-top: 20px;
    }

    @media (max-width: 480px) {
      .content {
        padding: 48px 28px;
      }

      .symbol {
        font-size: 56px;
        margin-bottom: 24px;
      }

      .checkmark {
        width: 64px;
        height: 64px;
        font-size: 36px;
        margin-bottom: 24px;
      }
    }
  `],
})
export class StartOnboardingComponent implements OnInit, OnDestroy {
  ready = false;
  err   = '';
  statusText = 'Analyzing your profile';
  private pollTimer: any;
  private statusTimer: any;
  private isBrowser: boolean;

  private statusMessages = [
    'Analyzing your profile',
    'Finding compatible matches',
    'Reviewing shared interests',
    'Preparing your deck'
  ];
  private statusIndex = 0;

  constructor(
    private http: HttpClient,
    private router: Router,
    private cdr: ChangeDetectorRef,
    @Inject(PLATFORM_ID) platformId: object,
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  ngOnInit() {
    if (!this.isBrowser) return;
    this.pollStatus();
    this.rotateStatus();
  }

  ngOnDestroy() {
    clearInterval(this.pollTimer);
    clearInterval(this.statusTimer);
  }

  private rotateStatus() {
    this.statusTimer = setInterval(() => {
      this.statusIndex = (this.statusIndex + 1) % this.statusMessages.length;
      this.statusText = this.statusMessages[this.statusIndex];
      this.cdr.markForCheck();
    }, 2500);
  }

  private pollStatus() {
    this.pollTimer = setInterval(async () => {
      try {
        const state: any = await firstValueFrom(
          this.http.get(`${environment.apiUrl}/onboarding/state`)
        );
        if (state.profileStatus === 'COMPLETE') {
          this.ready = true;
          clearInterval(this.pollTimer);
          clearInterval(this.statusTimer);
          this.cdr.markForCheck();
        }
      } catch { /* keep polling */ }
    }, 3000);

    // Fallback — show ready after 15s regardless
    setTimeout(() => {
      if (!this.ready) {
        this.ready = true;
        clearInterval(this.statusTimer);
        this.cdr.markForCheck();
      }
    }, 15_000);
  }

  enter() { this.router.navigateByUrl('/moments'); }
}
