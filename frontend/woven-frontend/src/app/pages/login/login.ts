import {
  AfterViewInit, OnDestroy, Component, ElementRef,
  Inject, NgZone, PLATFORM_ID, ChangeDetectionStrategy,
  ChangeDetectorRef, ViewChild,
} from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

declare global { interface Window { google: any; } }

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './login.html',
  styleUrls: ['./login.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent implements AfterViewInit, OnDestroy {
  @ViewChild('introEl')    introRef?:  ElementRef<HTMLDivElement>;
  @ViewChild('introVideo') videoRef?:  ElementRef<HTMLVideoElement>;
  @ViewChild('cardEl')     cardRef?:   ElementRef<HTMLDivElement>;

  isLoading = false;
  errorMsg  = '';
  agreed    = false;

  private isBrowser: boolean;
  private spotlightHandler?: (e: MouseEvent) => void;
  private hideIntroTimer?: ReturnType<typeof setTimeout>;

  constructor(
    private http: HttpClient,
    private router: Router,
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    @Inject(PLATFORM_ID) platformId: object,
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  ngAfterViewInit(): void {
    if (!this.isBrowser) return;

    // Fallback: show card if video never fires 'ended'
    this.hideIntroTimer = setTimeout(() => this.showCard(), 13_000);

    // Delay slightly so SSR hydration fully settles before we touch the video
    setTimeout(() => this.startVideo(), 200);
  }

  private startVideo(): void {
    const video = this.videoRef?.nativeElement;
    if (!video) {
      // ViewChild not resolved — go straight to card
      this.showCard();
      return;
    }

    video.muted = true;
    video.currentTime = 0;

    const tryPlay = () => {
      video.play().catch((err: any) => {
        console.warn('[Login] video.play() blocked:', err);
        // Don't skip — leave skip button and 13s timer as fallback
      });
    };

    // If already has data, play immediately; otherwise wait for canplay
    if (video.readyState >= 3) {
      tryPlay();
    } else {
      video.addEventListener('canplay', tryPlay, { once: true });
    }
  }

  ngOnDestroy(): void {
    clearTimeout(this.hideIntroTimer);
    if (this.spotlightHandler) {
      document.removeEventListener('mousemove', this.spotlightHandler);
    }
  }

  // Called by (ended) on the video element
  protected onVideoEnd(): void {
    clearTimeout(this.hideIntroTimer);
    this.showCard();
  }

  protected toggleAgree(): void {
    this.agreed = !this.agreed;
    this.cdr.markForCheck();
  }

  protected onLockedClick(): void {
    // Briefly shake the checkbox to draw attention
    const label = document.querySelector('.consent');
    if (label) {
      label.classList.add('shake');
      setTimeout(() => label.classList.remove('shake'), 500);
    }
  }

  protected skipIntro(): void {
    clearTimeout(this.hideIntroTimer);
    this.showCard(true);
  }

  private showCard(immediate = false): void {
    const intro = this.introRef?.nativeElement;
    const card  = this.cardRef?.nativeElement;

    if (immediate) {
      if (intro) intro.style.display = 'none';
      if (card)  { card.style.opacity = '1'; card.style.transform = 'none'; }
      this.setupSpotlight(card);
      setTimeout(() => this.renderGoogleButton(), 80);
      return;
    }

    // Fade intro out over 0.8s (CSS transition), then hide and reveal card
    if (intro) intro.classList.add('fade-out');

    setTimeout(() => {
      if (intro) intro.style.display = 'none';
      if (card)  card.classList.add('visible');
      this.setupSpotlight(card);
      this.renderGoogleButton();
    }, 800);
  }

  private setupSpotlight(card: HTMLElement | undefined): void {
    if (!card) return;
    this.spotlightHandler = (e: MouseEvent) => {
      const rect = card.getBoundingClientRect();
      card.style.setProperty('--cx', `${e.clientX - rect.left}px`);
      card.style.setProperty('--cy', `${e.clientY - rect.top}px`);
    };
    document.addEventListener('mousemove', this.spotlightHandler);
  }

  private renderGoogleButton(): void {
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
          this.zone.run(() => this.router.navigateByUrl('/app', { replaceUrl: true }));
        },
        error: () => {
          this.isLoading = false;
          this.errorMsg  = 'Login failed. Please try again.';
          this.cdr.markForCheck();
        },
      });
  }
}
