import {
  AfterViewInit, OnDestroy, Component, ElementRef,
  Inject, NgZone, PLATFORM_ID, ChangeDetectionStrategy,
  ChangeDetectorRef, ViewChild,
} from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { environment } from '../../../environments/environment';

declare global { interface Window { google: any; } }

// ── Story canvas types ──────────────────────────────────────────────

interface Orb {
  x: number; y: number;
  r: number;
  opacity: number;
}

interface Heart {
  x: number; y: number;
  vx: number; vy: number;
  size: number;
  opacity: number;
}

interface StoryState {
  sparkleOpacity: number;  // flickers at the "notice" moment
  mergeGlow: number;       // 0→1 at meeting, drives a central white burst
}

// ── Component ───────────────────────────────────────────────────────

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, CardModule, ButtonModule],
  templateUrl: './login.html',
  styleUrls: ['./login.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent implements AfterViewInit, OnDestroy {
  @ViewChild('storyCanvas') storyCanvasRef?: ElementRef<HTMLCanvasElement>;

  isLoading = false;
  errorMsg  = '';

  private isBrowser: boolean;
  private gsapLib: any;
  private mainTimeline: any;
  private cardFloat: any;
  private storyRafId = 0;
  private storyRunning = false;
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

    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    setTimeout(() => this.renderGoogleButton(), reduced ? 400 : 5500);

    if (reduced) {
      const story = document.querySelector('.story') as HTMLElement;
      if (story) story.style.display = 'none';
      const card  = document.querySelector('.card')  as HTMLElement;
      if (card)  { card.style.opacity = '1'; card.style.transform = 'none'; }
      return;
    }

    const { gsap } = await import('gsap');
    this.gsapLib = gsap;
    this.runLoveStory(gsap);
  }

  ngOnDestroy() {
    this.mainTimeline?.kill();
    this.cardFloat?.kill();
    this.storyRunning = false;
    cancelAnimationFrame(this.storyRafId);
    if (this.spotlightHandler) {
      document.removeEventListener('mousemove', this.spotlightHandler);
    }
  }

  // ── Skip handler (called from template) ───────────────────────────

  protected skipIntro() {
    const gsap = this.gsapLib;
    if (!gsap) return;

    this.mainTimeline?.kill();
    this.storyRunning = false;
    cancelAnimationFrame(this.storyRafId);

    const story = document.querySelector('.story') as HTMLElement;
    if (story) gsap.to(story, { opacity: 0, duration: 0.25, onComplete: () => story.style.display = 'none' });

    gsap.set('.card', { y: 0, scale: 1, opacity: 1 });
    this.cardFloat = gsap.to('.card', { y: -7, duration: 2.9, ease: 'sine.inOut', repeat: -1, yoyo: true });
    this.setupSpotlight(gsap);
  }

  // ── Main love story animation ─────────────────────────────────────

  private runLoveStory(gsap: any) {
    const canvas = this.storyCanvasRef?.nativeElement;
    if (!canvas) return;

    const W = window.innerWidth;
    const H = window.innerHeight;
    canvas.width  = W;
    canvas.height = H;

    const ctx = canvas.getContext('2d')!;

    // Character state objects — GSAP tweens these, rAF reads them
    const rose: Orb  = { x: W * 0.22, y: H * 0.5, r: 0,  opacity: 0 };
    const plum: Orb  = { x: W * 0.78, y: H * 0.5, r: 0,  opacity: 0 };
    const hearts: Heart[] = [];
    const state: StoryState = { sparkleOpacity: 0, mergeGlow: 0 };

    // Start the render loop
    this.startRenderLoop(ctx, W, H, rose, plum, hearts, state);

    // ── Timeline ──────────────────────────────────────────────────

    const tl = gsap.timeline();
    this.mainTimeline = tl;

    // Store initial wander origins for reference
    const rX0 = rose.x, rY0 = rose.y;
    const pX0 = plum.x, pY0 = plum.y;

    // ── Act 1: Appear (0.1–0.5s) ─────────────────────────────────
    tl.to(rose, { r: 22, opacity: 1, duration: 0.55, ease: 'back.out(2)' }, 0.1);
    tl.to(plum, { r: 22, opacity: 1, duration: 0.55, ease: 'back.out(2)' }, 0.3);

    // Skip-btn fades in at 0.8s
    gsap.to('.skip-btn', { opacity: 1, duration: 0.4, ease: 'power2.out', delay: 0.8 });

    // Side breathing (separate infinite tweens, killed at notice)
    const roseBreathe = gsap.to(rose, {
      r: 27, duration: 0.95, ease: 'sine.inOut', repeat: -1, yoyo: true, delay: 0.7,
    });
    const plumBreathe = gsap.to(plum, {
      r: 27, duration: 1.15, ease: 'sine.inOut', repeat: -1, yoyo: true, delay: 0.9,
    });

    // ── Act 2: Wander (0.8–1.8s) ─────────────────────────────────
    // Rose drifts aimlessly
    tl.to(rose, { x: rX0 - 28, y: rY0 + 22, duration: 0.42, ease: 'sine.inOut' }, 0.82);
    tl.to(rose, { x: rX0 + 18, y: rY0 - 25, duration: 0.42, ease: 'sine.inOut' }, 1.24);
    tl.to(rose, { x: rX0,      y: rY0,      duration: 0.30, ease: 'sine.inOut' }, 1.66);

    // Plum drifts in a different pattern
    tl.to(plum, { x: pX0 + 25, y: pY0 - 18, duration: 0.42, ease: 'sine.inOut' }, 0.82);
    tl.to(plum, { x: pX0 - 20, y: pY0 + 28, duration: 0.42, ease: 'sine.inOut' }, 1.24);
    tl.to(plum, { x: pX0,      y: pY0,      duration: 0.30, ease: 'sine.inOut' }, 1.66);

    // ── Act 3: Notice (1.8–2.2s) ─────────────────────────────────
    tl.call(() => { roseBreathe.kill(); plumBreathe.kill(); }, [], 1.78);

    // Startled pulse
    tl.to(rose, { r: 32, duration: 0.14, ease: 'back.out(4)' }, 1.80);
    tl.to(rose, { r: 22, duration: 0.28, ease: 'power2.out' }, 1.94);
    tl.to(plum, { r: 32, duration: 0.14, ease: 'back.out(4)' }, 1.80);
    tl.to(plum, { r: 22, duration: 0.28, ease: 'power2.out' }, 1.94);

    // Sparkle between them
    tl.to(state, { sparkleOpacity: 1, duration: 0.18, ease: 'power2.out' }, 1.86);
    tl.to(state, { sparkleOpacity: 0, duration: 0.22, ease: 'power2.in'  }, 2.04);

    // ── Act 4: Approach (2.2–3.3s) ───────────────────────────────
    tl.to(rose, { x: W / 2 - 38, y: H / 2, duration: 1.12, ease: 'power2.inOut' }, 2.20);
    tl.to(plum, { x: W / 2 + 38, y: H / 2, duration: 1.12, ease: 'power2.inOut' }, 2.20);

    // Their glows grow as they get closer
    tl.to(rose, { r: 26, duration: 1.12, ease: 'power2.inOut' }, 2.20);
    tl.to(plum, { r: 26, duration: 1.12, ease: 'power2.inOut' }, 2.20);

    // ── Act 5: Orbit (3.3–3.95s) ─────────────────────────────────
    const orb = { a: 0 };
    tl.to(orb, {
      a: Math.PI * 2,
      duration: 0.65,
      ease: 'none',
      onUpdate: () => {
        const R = 34;
        rose.x = W / 2 + Math.cos(orb.a) * R;
        rose.y = H / 2 + Math.sin(orb.a) * R * 0.55;
        plum.x = W / 2 + Math.cos(orb.a + Math.PI) * R;
        plum.y = H / 2 + Math.sin(orb.a + Math.PI) * R * 0.55;
      },
    }, 3.30);

    // ── Act 6: Meeting (3.95–4.3s) ───────────────────────────────
    tl.call(() => this.spawnHearts(W, H, hearts), [], 3.95);

    // Both race to center and merge
    tl.to(rose, { x: W / 2, y: H / 2, r: 70, opacity: 0, duration: 0.42, ease: 'power2.out' }, 3.95);
    tl.to(plum, { x: W / 2, y: H / 2, r: 70, opacity: 0, duration: 0.42, ease: 'power2.out' }, 3.95);

    // Central merge glow burst then fade
    tl.to(state, { mergeGlow: 1, duration: 0.18, ease: 'power4.out' }, 3.95);
    tl.to(state, { mergeGlow: 0, duration: 0.55, ease: 'power2.out' }, 4.13);

    // ── Act 7: Woven (4.2–5.2s) ──────────────────────────────────
    // Letters stagger in
    tl.to('.wordmark span', {
      y: 0, opacity: 1,
      stagger: 0.075, duration: 0.42, ease: 'back.out(1.6)',
    }, 4.22);

    // Brief shimmer wave across letters
    tl.to('.wordmark span', {
      filter: 'brightness(2.2) drop-shadow(0 0 10px rgba(224,84,144,0.8))',
      stagger: 0.06, duration: 0.20, ease: 'power2.inOut',
      yoyo: true, repeat: 1,
    }, 4.75);

    // Fade story canvas + skip btn
    tl.to('.story', { opacity: 0, duration: 0.55, ease: 'power2.inOut',
      onComplete: () => {
        this.storyRunning = false;
        const story = document.querySelector('.story') as HTMLElement;
        if (story) story.style.display = 'none';
      },
    }, 4.55);

    // Card springs in (overlaps canvas fade for smooth dissolve feel)
    tl.to('.card', {
      y: 0, scale: 1, opacity: 1,
      duration: 0.90, ease: 'back.out(1.3)',
      onComplete: () => {
        this.cardFloat = gsap.to('.card', {
          y: -7, duration: 2.9, ease: 'sine.inOut', repeat: -1, yoyo: true,
        });
        this.setupSpotlight(gsap);
      },
    }, 4.45);

    // Initial card state
    gsap.set('.card', { y: 52, scale: 0.95, opacity: 0 });
    gsap.set('.wordmark span', { y: 22, opacity: 0 });
    gsap.set('.skip-btn', { opacity: 0 });
  }

  // ── Canvas render loop ────────────────────────────────────────────

  private startRenderLoop(
    ctx: CanvasRenderingContext2D,
    W: number, H: number,
    rose: Orb, plum: Orb,
    hearts: Heart[],
    state: StoryState,
  ) {
    this.storyRunning = true;
    const loop = () => {
      if (!this.storyRunning) return;
      this.drawFrame(ctx, W, H, rose, plum, hearts, state);
      this.storyRafId = requestAnimationFrame(loop);
    };
    loop();
  }

  private drawFrame(
    ctx: CanvasRenderingContext2D,
    W: number, H: number,
    rose: Orb, plum: Orb,
    hearts: Heart[],
    state: StoryState,
  ) {
    // Trail persistence — paint semi-transparent bg instead of clearing
    ctx.globalCompositeOperation = 'source-over';
    ctx.fillStyle = 'rgba(26, 15, 30, 0.16)';
    ctx.fillRect(0, 0, W, H);

    // Draw rose orb
    if (rose.opacity > 0) this.drawOrb(ctx, rose, '#E05490', 'rgba(224,84,144,0.42)');

    // Draw plum orb
    if (plum.opacity > 0) this.drawOrb(ctx, plum, '#7D5BD0', 'rgba(125,91,208,0.42)');

    // Notice sparkle ✦
    if (state.sparkleOpacity > 0) {
      ctx.save();
      ctx.globalAlpha = state.sparkleOpacity;
      ctx.font = '28px serif';
      ctx.fillStyle = '#fff5fa';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText('✦', W / 2, H / 2);
      ctx.restore();
    }

    // Merge glow burst at center
    if (state.mergeGlow > 0) {
      const cx = W / 2, cy = H / 2;
      const r = state.mergeGlow * 180;
      const grad = ctx.createRadialGradient(cx, cy, 0, cx, cy, r);
      grad.addColorStop(0,    `rgba(255,245,250,${state.mergeGlow * 0.95})`);
      grad.addColorStop(0.25, `rgba(224,84,144,${state.mergeGlow * 0.65})`);
      grad.addColorStop(0.60, `rgba(125,91,208,${state.mergeGlow * 0.30})`);
      grad.addColorStop(1,    'rgba(0,0,0,0)');
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.fillStyle = grad;
      ctx.fill();
    }

    // Hearts
    ctx.save();
    for (const h of hearts) {
      if (h.opacity <= 0) continue;
      ctx.globalAlpha = h.opacity;
      ctx.font = `${h.size}px serif`;
      ctx.fillStyle = '#F07AAC';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText('♡', h.x, h.y);

      // Physics tick
      h.x  += h.vx  * 0.016;
      h.y  += h.vy  * 0.016;
      h.vy += 18    * 0.016; // gentle gravity
      h.vx *= 0.985;
      h.opacity -= 0.010;
    }
    ctx.restore();
  }

  private drawOrb(ctx: CanvasRenderingContext2D, orb: Orb, color: string, glow: string) {
    ctx.save();
    ctx.globalAlpha = orb.opacity;

    // Outer atmospheric glow (large, soft)
    const outerR = orb.r * 4;
    const outerGrad = ctx.createRadialGradient(orb.x, orb.y, 0, orb.x, orb.y, outerR);
    outerGrad.addColorStop(0,   glow);
    outerGrad.addColorStop(0.5, glow.replace('0.42', '0.16').replace('0.42', '0.16'));
    outerGrad.addColorStop(1,   'rgba(0,0,0,0)');
    ctx.beginPath();
    ctx.arc(orb.x, orb.y, outerR, 0, Math.PI * 2);
    ctx.fillStyle = outerGrad;
    ctx.fill();

    // Core sphere with highlight
    const coreGrad = ctx.createRadialGradient(
      orb.x - orb.r * 0.25, orb.y - orb.r * 0.25, 0,
      orb.x, orb.y, orb.r,
    );
    coreGrad.addColorStop(0,   'rgba(255,245,250,0.95)');
    coreGrad.addColorStop(0.35, color);
    coreGrad.addColorStop(1,   'rgba(0,0,0,0.25)');
    ctx.beginPath();
    ctx.arc(orb.x, orb.y, orb.r, 0, Math.PI * 2);
    ctx.fillStyle = coreGrad;
    ctx.fill();

    ctx.restore();
  }

  // ── Spawn helpers ─────────────────────────────────────────────────

  private spawnHearts(W: number, H: number, hearts: Heart[]) {
    const cx = W / 2, cy = H / 2;
    for (let i = 0; i < 18; i++) {
      const angle = (i / 18) * Math.PI * 2 + (Math.random() - 0.5) * 0.4;
      const speed = 60 + Math.random() * 130;
      hearts.push({
        x:       cx,
        y:       cy,
        vx:      Math.cos(angle) * speed * 0.55,
        vy:      Math.sin(angle) * speed - 70,
        size:    12 + Math.random() * 14,
        opacity: 0.88 + Math.random() * 0.12,
      });
    }
  }

  // ── Card spotlight ────────────────────────────────────────────────

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
      theme: 'outline', size: 'large', width: 360, text: 'continue_with',
    });
  }

  private onGoogleCredential(resp: any): void {
    this.errorMsg  = '';
    this.isLoading = true;
    const idToken  = resp?.credential;
    if (!idToken) {
      this.isLoading = false;
      this.errorMsg  = 'Google token missing. Please try again.';
      this.cdr.markForCheck();
      return;
    }
    this.http
      .post<{ accessToken: string; user: any }>(`${environment.apiUrl}/auth/google`, { idToken })
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
