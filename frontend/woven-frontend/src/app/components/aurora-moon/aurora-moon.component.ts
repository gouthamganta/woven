import { Component, OnInit, OnDestroy, PLATFORM_ID, Inject } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';

@Component({
  selector: 'app-aurora-moon',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './aurora-moon.component.html',
  styleUrls: ['./aurora-moon.component.scss']
})
export class AuroraMoonComponent implements OnInit, OnDestroy {
  private scrollListener?: () => void;
  private scrollTimeout?: ReturnType<typeof setTimeout>;
  isBrowser: boolean;

  constructor(@Inject(PLATFORM_ID) platformId: Object) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  ngOnInit(): void {
    if (!this.isBrowser) return;

    // Moon rolls from top-right to bottom-left as you scroll
    this.scrollListener = () => {
      const moon = document.querySelector('.aurora-moon') as HTMLElement;
      const aurora = document.querySelector('.aurora-waves') as HTMLElement;

      if (!moon || !aurora) return;

      // Calculate scroll progress (0 = top, 1 = bottom)
      const scrollHeight = document.documentElement.scrollHeight - window.innerHeight;
      const scrollProgress = Math.min(window.scrollY / scrollHeight, 1);

      // Position: top-right (10%, 10%) → bottom-left (85%, 10%)
      const topPosition = 10 + (scrollProgress * 75); // 10% → 85%
      const rightPosition = 90 - (scrollProgress * 80); // 90% → 10%

      // Rotation: simulate rolling (360deg per full scroll)
      const rotation = scrollProgress * 360;

      moon.style.top = `${topPosition}%`;
      moon.style.right = `${rightPosition}%`;
      moon.style.transform = `rotate(${rotation}deg)`;

      // Add active class when scrolling
      moon.classList.add('scrolling');
      aurora.classList.add('scrolling');

      // Remove after scroll stops
      clearTimeout(this.scrollTimeout);
      this.scrollTimeout = setTimeout(() => {
        moon.classList.remove('scrolling');
        aurora.classList.remove('scrolling');
      }, 150);
    };

    window.addEventListener('scroll', this.scrollListener, { passive: true });
  }

  ngOnDestroy(): void {
    if (this.scrollListener) {
      window.removeEventListener('scroll', this.scrollListener);
    }
    clearTimeout(this.scrollTimeout);
  }
}
