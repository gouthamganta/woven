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
    <div class="celebrate">
      <div class="bg"></div>

      <div class="card">
        <div class="shimmer"></div>

        <div class="symbol">◈</div>
        <h1 class="headline">You're in.</h1>
        <p class="sub">We're putting together your first deck…</p>

        <div class="pulse" [class.ready]="ready">
          <div class="pulseRing"></div>
          <div class="pulseRing delay1"></div>
          <div class="pulseRing delay2"></div>
          <span class="pulseLabel" *ngIf="!ready">Finding your matches</span>
          <span class="pulseLabel ready" *ngIf="ready">Your deck is ready</span>
        </div>

        <button class="cta" *ngIf="ready" (click)="enter()">
          See your first deck →
        </button>

        <p class="err" *ngIf="err">{{ err }}</p>
      </div>

      <!-- Canvas confetti -->
      <canvas class="confetti" #confettiCanvas></canvas>
    </div>
  `,
  styles: [`
    .celebrate {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 24px;
      position: relative;
      overflow: hidden;
    }

    .bg {
      position: fixed;
      inset: 0;
      background: radial-gradient(ellipse 80% 60% at 50% 40%, rgba(212,160,23,0.08) 0%, transparent 70%),
                  radial-gradient(ellipse 60% 40% at 30% 70%, rgba(192,57,43,0.06) 0%, transparent 60%),
                  var(--bg-base);
    }

    .confetti {
      position: fixed;
      inset: 0;
      pointer-events: none;
      z-index: 1;
    }

    .card {
      position: relative;
      z-index: 2;
      width: min(440px, 100%);
      background: rgba(34, 22, 40, 0.88);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-2xl);
      padding: 48px 36px;
      text-align: center;
      backdrop-filter: blur(24px);
      -webkit-backdrop-filter: blur(24px);
      box-shadow: var(--shadow-lg), 0 0 60px rgba(212,160,23,0.08);
    }

    .shimmer {
      position: absolute;
      top: 0; left: 15%; right: 15%;
      height: 1px;
      background: linear-gradient(90deg, transparent, var(--gold-400), var(--rose-400), transparent);
      border-radius: 9999px;
    }

    .symbol {
      font-size: 48px;
      margin-bottom: 16px;
      animation: symbolPulse 2s ease-in-out infinite;
    }

    @keyframes symbolPulse {
      0%, 100% { opacity: 0.6; transform: scale(1); }
      50%       { opacity: 1;   transform: scale(1.08); }
    }

    .headline {
      font-family: var(--font-display);
      font-size: 40px;
      font-weight: 300;
      letter-spacing: -0.03em;
      margin: 0 0 10px;
      background: linear-gradient(135deg, var(--gold-300) 0%, var(--text-primary) 50%, var(--rose-300) 100%);
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
    }

    .sub {
      font-family: var(--font-ui);
      font-size: 14px;
      color: var(--text-muted);
      margin: 0 0 36px;
      line-height: 1.5;
    }

    .pulse {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
      margin-bottom: 32px;
      position: relative;
    }

    .pulseRing {
      position: absolute;
      width: 56px; height: 56px;
      border-radius: 50%;
      border: 1.5px solid rgba(212,160,23,0.4);
      animation: rippleOut 2s ease-out infinite;
    }
    .pulseRing.delay1 { animation-delay: 0.6s; }
    .pulseRing.delay2 { animation-delay: 1.2s; }

    @keyframes rippleOut {
      0%   { transform: scale(0.6); opacity: 1; }
      100% { transform: scale(2.4); opacity: 0; }
    }

    .pulseLabel {
      font-family: var(--font-ui);
      font-size: 13px;
      color: var(--text-muted);
      margin-top: 44px;
    }
    .pulseLabel.ready { color: var(--gold-300); font-weight: 600; }

    .pulse.ready .pulseRing { border-color: rgba(212,160,23,0.6); animation-duration: 1s; }

    .cta {
      width: 100%;
      padding: 16px 24px;
      border: none;
      border-radius: var(--radius-xl);
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base);
      font-family: var(--font-ui);
      font-size: 15px;
      font-weight: 700;
      cursor: pointer;
      transition: opacity 0.2s ease, transform 0.15s ease;
      box-shadow: 0 4px 20px rgba(212,160,23,0.35);
      animation: ctaFadeIn 0.5s ease forwards;
    }
    @keyframes ctaFadeIn { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: none; } }
    .cta:hover { opacity: 0.88; }
    .cta:active { transform: scale(0.96); }

    .err { font-size: 12px; color: var(--rose-300); margin-top: 12px; }
  `],
})
export class StartOnboardingComponent implements OnInit, OnDestroy {
  ready = false;
  err   = '';
  private pollTimer: any;
  private isBrowser: boolean;

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
    this.launchConfetti();
    this.pollStatus();
  }

  ngOnDestroy() {
    clearInterval(this.pollTimer);
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
          this.cdr.markForCheck();
        }
      } catch { /* keep polling */ }
    }, 3000);

    // Fallback — show ready after 15s regardless
    setTimeout(() => {
      if (!this.ready) { this.ready = true; this.cdr.markForCheck(); }
    }, 15_000);
  }

  enter() { this.router.navigateByUrl('/moments'); }

  private launchConfetti() {
    const canvas = document.querySelector('.confetti') as HTMLCanvasElement;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    canvas.width  = window.innerWidth;
    canvas.height = window.innerHeight;

    const colors = ['#D4A017','#C0392B','#7F77DD','#F5F0E8','#E8C35A'];
    const particles: any[] = [];

    for (let i = 0; i < 120; i++) {
      particles.push({
        x: Math.random() * canvas.width,
        y: Math.random() * canvas.height - canvas.height,
        r: Math.random() * 6 + 3,
        color: colors[Math.floor(Math.random() * colors.length)],
        vy: Math.random() * 3 + 1.5,
        vx: (Math.random() - 0.5) * 2,
        rot: Math.random() * 360,
        rSpeed: (Math.random() - 0.5) * 4,
        alpha: 1,
      });
    }

    let frame = 0;
    const animate = () => {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      frame++;
      for (const p of particles) {
        p.y   += p.vy;
        p.x   += p.vx;
        p.rot += p.rSpeed;
        if (frame > 120) p.alpha = Math.max(0, p.alpha - 0.01);
        ctx.save();
        ctx.globalAlpha = p.alpha;
        ctx.fillStyle = p.color;
        ctx.translate(p.x, p.y);
        ctx.rotate(p.rot * Math.PI / 180);
        ctx.fillRect(-p.r / 2, -p.r / 2, p.r, p.r * 1.6);
        ctx.restore();
      }
      if (particles.some(p => p.alpha > 0)) requestAnimationFrame(animate);
      else ctx.clearRect(0, 0, canvas.width, canvas.height);
    };
    animate();
  }
}
