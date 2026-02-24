import { AfterViewInit, Component, Inject, NgZone, PLATFORM_ID, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient, HttpClientModule } from '@angular/common/http';

// PrimeNG
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';

import { environment } from '../../../environments/environment';

declare global {
  interface Window {
    google: any;
  }
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    CommonModule,
    HttpClientModule,
    CardModule,
    ButtonModule
  ],
  templateUrl: './login.html',
  styleUrls: ['./login.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent implements AfterViewInit {
  isLoading = false;
  errorMsg = '';
  private isBrowser: boolean;
  
  // Pointer tracking for spotlight effect
  mx = 0;
  my = 0;

  constructor(
    private http: HttpClient,
    private router: Router,
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    @Inject(PLATFORM_ID) platformId: object
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  ngAfterViewInit(): void {
    if (!this.isBrowser) return;

    // Set default spotlight to center so it looks good even without movement
    this.setPointer(window.innerWidth * 0.5, window.innerHeight * 0.42);

    // ✅ DELAY Google button rendering until after intro animation (6.5 seconds)
    setTimeout(() => {
      if (!window.google?.accounts?.id) {
        this.errorMsg = 'Google Sign-In failed to load. Please refresh.';
        return;
      }

      window.google.accounts.id.initialize({
        client_id: environment.googleClientId,
        callback: (resp: any) => this.onGoogleCredential(resp)
      });

      const host = document.getElementById('googleBtn');
      if (!host) return;

      window.google.accounts.id.renderButton(host, {
        theme: 'outline',
        size: 'large',
        width: 360,
        text: 'continue_with'
      });
    }, 6500); // Wait for intro animation to complete (6.2s) + small buffer
  }

  private setPointer(x: number, y: number): void {
    // Keep values stable even if something odd comes in
    this.mx = Math.max(0, Math.floor(x));
    this.my = Math.max(0, Math.floor(y));
  }

  onMove(ev: MouseEvent): void {
    if (!this.isBrowser) return;
    this.setPointer(ev.clientX, ev.clientY);
  }

  onTouchMove(ev: TouchEvent): void {
    if (!this.isBrowser) return;
    const t = ev.touches?.[0];
    if (!t) return;
    this.setPointer(t.clientX, t.clientY);
  }

  private onGoogleCredential(resp: any): void {
    this.errorMsg = '';
    this.isLoading = true;

    const idToken = resp?.credential;
    if (!idToken) {
      this.isLoading = false;
      this.errorMsg = 'Google token missing. Please try again.';
      return;
    }

    this.http
      .post<{ accessToken: string; user: any }>(
        `${environment.apiUrl}/auth/google`,
        { idToken }
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
          this.errorMsg = 'Login failed. Please try again.';
          this.cdr.markForCheck();
        }
      });
  }
}