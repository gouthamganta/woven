import { Component, signal, OnInit, OnDestroy, HostListener, Inject, PLATFORM_ID, ChangeDetectionStrategy } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { isPlatformBrowser } from '@angular/common';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class App implements OnInit, OnDestroy {
  protected readonly title = signal('woven-frontend');

  private rafId: number | null = null;
  private lastScrollY = 0;
  private isBrowser = false;
  private scrollThrottled = false;

  constructor(@Inject(PLATFORM_ID) platformId: object) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  ngOnInit() {
    if (!this.isBrowser) return;

    this.lastScrollY = window.scrollY || 0;
    this.scheduleUpdate();
  }

  ngOnDestroy() {
    if (!this.isBrowser) return;
    if (this.rafId) cancelAnimationFrame(this.rafId);
  }

  @HostListener('window:scroll')
  onScroll() {
    if (!this.isBrowser || this.scrollThrottled) return;

    this.scrollThrottled = true;
    this.lastScrollY = window.scrollY || 0;
    this.scheduleUpdate();

    // Throttle to ~30fps instead of every scroll event
    setTimeout(() => { this.scrollThrottled = false; }, 33);
  }

  private scheduleUpdate() {
    if (!this.isBrowser) return;
    if (this.rafId) return;

    this.rafId = requestAnimationFrame(() => {
      this.rafId = null;

      const y = this.lastScrollY;
      const doc = document.documentElement;

      // Subtle drift
      const wx = (Math.sin(y / 900) * 10).toFixed(2);
      const wy = (-(y / 140)).toFixed(2);

      // Scroll progress 0..1
      const maxScroll = Math.max(1, document.body.scrollHeight - window.innerHeight);
      const scroll = Math.min(1, Math.max(0, y / maxScroll)).toFixed(4);

      doc.style.setProperty('--wx', `${wx}px`);
      doc.style.setProperty('--wy', `${wy}px`);
      doc.style.setProperty('--scroll', `${scroll}`);
    });
  }
}
