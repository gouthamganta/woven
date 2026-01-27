import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: `<router-outlet></router-outlet>`,
})
export class AppComponent implements OnInit, OnDestroy {
  private rafId: number | null = null;
  private lastScrollY = 0;

  ngOnInit() {
    this.lastScrollY = window.scrollY || 0;
    this.scheduleUpdate();
  }

  ngOnDestroy() {
    if (this.rafId) cancelAnimationFrame(this.rafId);
  }

  @HostListener('window:scroll')
  onScroll() {
    this.lastScrollY = window.scrollY || 0;
    this.scheduleUpdate();
  }

  private scheduleUpdate() {
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