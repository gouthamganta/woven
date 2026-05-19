import {
  AfterViewInit, OnDestroy, Component, ElementRef, Inject,
  NgZone, PLATFORM_ID, ChangeDetectionStrategy, ChangeDetectorRef, ViewChild,
} from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { environment } from '../../../environments/environment';

declare global {
  interface Window { google: any; }
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, CardModule, ButtonModule],
  templateUrl: './login.html',
  styleUrls: ['./login.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent implements AfterViewInit, OnDestroy {
  @ViewChild('burstCanvas') burstCanvasRef?: ElementRef<HTMLCanvasElement>;

  isLoading = false;
  errorMsg  = '';

  private isBrowser: boolean;
  private gsapTimeline: any;
  private cardFloat: any;
  private spotlightHandler?: (e: MouseEvent) => void;

  constructor(
    private http: HttpClient,
    private router: Router,
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    @Inject(PLATFORM_ID) platformId: object,
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  async ngAfterViewInit(): Promise<void> {
    if (!this.isBrowser) return;

    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // Google button delay — skip animation wait when reduced-motion is on
    const btnDelay = reducedMotion ? 400 : 5000;
    setTimeout(() => this.renderGoogleButton(), btnDelay);

    if (reducedMotion) {
      // Skip splash, show card immediately
      const splash = document.querySelector('.splash') as HTMLElement;
      if (splash) splash.style.display = 'none';
      const card = document.querySelector('.card') as HTMLElement;
      if (card) { card.style.opacity = '1'; card.style.transform = 'none'; }
      return;
    }

    const { gsap } = await import('gsap');

    // ── Set initial states before timeline starts ────────────────────
    const hw = window.innerWidth  * 0.47;
    gsap.set('.diamond-left',   { x: -hw,  scale: 0.5, rotation: -18, opacity: 0 });
    gsap.set('.diamond-right',  { x:  hw,  scale: 0.5, rotation:  18, opacity: 0 });
    gsap.set('.conjunction',    { scale: 0, opacity: 0 });
    gsap.set('.w-mark',         { scale: 1.55, opacity: 0, filter: 'blur(18px)' });
    gsap.set('.wordmark span',  { y: 26, opacity: 0 });
    gsap.set('.card',           { y: 52, scale: 0.95, opacity: 0 });

    // ── Main timeline ────────────────────────────────────────────────
    const tl = gsap.timeline();
    this.gsapTimeline = tl;

    // Stage 1 — Diamonds materialise at the edges (0.2s)
    tl.to('.diamond-left', {
      opacity: 1, scale: 1, rotation: 0,
      duration: 0.4, ease: 'back.out(1.6)',
    }, 0.2);
    tl.to('.diamond-right', {
      opacity: 1, scale: 1, rotation: 0,
      duration: 0.4, ease: 'back.out(1.6)',
    }, 0.38);

    // Stage 2 — Diamonds travel inward with spring deceleration (0.6s)
    tl.to('.diamond-left',  { x: -32, duration: 1.55, ease: 'power3.inOut' }, 0.6);
    tl.to('.diamond-right', { x:  32, duration: 1.55, ease: 'power3.inOut' }, 0.6);

    // Final merge into center (2.05s)
    tl.to(['.diamond-left', '.diamond-right'], {
      x: 0, scale: 1.35, opacity: 0,
      duration: 0.32, ease: 'power2.in',
    }, 2.05);

    // Stage 3 — Conjunction flash + particle burst (2.35s)
    tl.to('.conjunction', {
      scale: 1, opacity: 1,
      duration: 0.13, ease: 'power4.out',
      onStart: () => this.fireBurst(),
    }, 2.35);
    tl.to('.conjunction', {
      scale: 9, opacity: 0,
      duration: 0.55, ease: 'expo.out',
    }, 2.48);

    // Stage 4 — W crystallises from the flash (2.75s)
    tl.to('.w-mark', {
      opacity: 1, scale: 1, filter: 'blur(0px)',
      duration: 0.72, ease: 'power3.out',
    }, 2.75);

    // Stage 5 — "Woven" letters stagger up (3.35s)
    tl.to('.wordmark span', {
      y: 0, opacity: 1,
      stagger: 0.072,
      duration: 0.46, ease: 'back.out(1.5)',
    }, 3.35);

    // W fades out as wordmark is now showing (3.6s)
    tl.to('.w-mark', {
      opacity: 0, scale: 0.9,
      duration: 0.28, ease: 'power2.in',
    }, 3.68);

    // Stage 6 — Card springs in (4.0s)
    tl.to('.card', {
      y: 0, scale: 1, opacity: 1,
      duration: 0.88, ease: 'back.out(1.35)',
      onComplete: () => {
        // Gentle floating loop starts once card is fully in
        this.cardFloat = gsap.to('.card', {
          y: -8, duration: 2.9,
          ease: 'sine.inOut', repeat: -1, yoyo: true,
        });
        this.setupSpotlight(gsap);
      },
    }, 4.0);

    // Splash fades out (4.2s — overlaps slightly with card entrance)
    tl.to('.splash', {
      opacity: 0, duration: 0.55, ease: 'power2.inOut',
      onComplete: () => {
        const splash = document.querySelector('.splash') as HTMLElement;
        if (splash) splash.style.display = 'none';
      },
    }, 4.2);
  }

  ngOnDestroy() {
    this.gsapTimeline?.kill();
    this.cardFloat?.kill();
    if (this.spotlightHandler) {
      document.removeEventListener('mousemove', this.spotlightHandler);
    }
  }

  // ── Particle burst at conjunction ─────────────────────────────────
  private fireBurst() {
    const canvas = this.burstCanvasRef?.nativeElement;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    canvas.width  = window.innerWidth;
    canvas.height = window.innerHeight;

    const cx = canvas.width  / 2;
    const cy = canvas.height / 2;
    const COUNT = 28;

    const particles = Array.from({ length: COUNT }, (_, i) => {
      const angle  = (i / COUNT) * Math.PI * 2;
      const speed  = 55 + Math.random() * 110;
      const isRose = i % 2 === 0;
      return {
        x:     cx,
        y:     cy,
        vx:    Math.cos(angle) * speed,
        vy:    Math.sin(angle) * speed,
        color: isRose ? '#E05490' : '#7D5BD0',
        r:     2.5 + Math.random() * 2,
      };
    });

    // Also add a few extra white streaks
    for (let i = 0; i < 6; i++) {
      const angle = Math.random() * Math.PI * 2;
      const speed = 80 + Math.random() * 60;
      particles.push({ x: cx, y: cy, vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed, color: '#fff5fa', r: 1.5 });
    }

    const DURATION = 750;
    let start: number | null = null;

    const draw = (ts: number) => {
      if (!start) start = ts;
      const p = Math.min((ts - start) / DURATION, 1);

      ctx.clearRect(0, 0, canvas.width, canvas.height);

      for (const pt of particles) {
        pt.x  += pt.vx * 0.016;
        pt.y  += pt.vy * 0.016;
        pt.vx *= 0.92;
        pt.vy *= 0.92;

        const life = 1 - p;
        ctx.beginPath();
        ctx.arc(pt.x, pt.y, pt.r * life + 0.5, 0, Math.PI * 2);
        ctx.fillStyle = pt.color;
        ctx.globalAlpha = life * 0.88;
        ctx.fill();
      }

      if (p < 1) {
        requestAnimationFrame(draw);
      } else {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
      }
    };

    requestAnimationFrame(draw);
  }

  // ── Mouse spotlight on card ───────────────────────────────────────
  private setupSpotlight(gsap: any) {
    const card = document.querySelector('.card') as HTMLElement;
    if (!card) return;

    const xSet = gsap.quickSetter(card, '--cx', 'px');
    const ySet = gsap.quickSetter(card, '--cy', 'px');

    this.spotlightHandler = (e: MouseEvent) => {
      const rect = card.getBoundingClientRect();
      xSet(e.clientX - rect.left);
      ySet(e.clientY - rect.top);
    };

    document.addEventListener('mousemove', this.spotlightHandler);
  }

  // ── Google button ─────────────────────────────────────────────────
  private renderGoogleButton() {
    if (!window.google?.accounts?.id) {
      this.errorMsg = 'Google Sign-In failed to load. Please refresh.';
      this.cdr.markForCheck();
      return;
    }

    window.google.accounts.id.initialize({
      client_id: environment.googleClientId,
      callback:  (resp: any) => this.onGoogleCredential(resp),
    });

    const host = document.getElementById('googleBtn');
    if (!host) return;

    window.google.accounts.id.renderButton(host, {
      theme: 'outline',
      size:  'large',
      width: 360,
      text:  'continue_with',
    });
  }

  private onGoogleCredential(resp: any): void {
    this.errorMsg  = '';
    this.isLoading = true;

    const idToken = resp?.credential;
    if (!idToken) {
      this.isLoading = false;
      this.errorMsg  = 'Google token missing. Please try again.';
      this.cdr.markForCheck();
      return;
    }

    this.http
      .post<{ accessToken: string; user: any }>(
        `${environment.apiUrl}/auth/google`,
        { idToken },
      )
      .subscribe({
        next: (res) => {
          localStorage.setItem('accessToken', res.accessToken);
          localStorage.setItem('user', JSON.stringify(res.user));
          this.isLoading = false;
          this.cdr.markForCheck();
          this.zone.run(() => this.router.navigateByUrl('/app'));
        },
        error: () => {
          this.isLoading = false;
          this.errorMsg  = 'Login failed. Please try again.';
          this.cdr.markForCheck();
        },
      });
  }
}
