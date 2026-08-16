import { Component, OnInit, OnDestroy, PLATFORM_ID, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { RouterModule } from '@angular/router';
import { gsap } from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';

// Register GSAP plugins
if (typeof window !== 'undefined') {
  gsap.registerPlugin(ScrollTrigger);
}

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingComponent implements OnInit, OnDestroy {
  isBrowser: boolean;

  constructor(@Inject(PLATFORM_ID) private platformId: Object) {
    this.isBrowser = isPlatformBrowser(this.platformId);
  }

  ngOnInit(): void {
    if (!this.isBrowser) return;

    // Initialize scroll animations after view renders
    requestAnimationFrame(() => this.initScrollAnimations());
  }

  ngOnDestroy(): void {
    if (this.isBrowser && typeof ScrollTrigger !== 'undefined') {
      ScrollTrigger.getAll().forEach(trigger => trigger.kill());
    }
  }

  private initScrollAnimations(): void {
    // Hero: Wordmark fade in + parallax
    gsap.from('.hero__wordmark', {
      scrollTrigger: {
        trigger: '.hero',
        start: 'top top',
        end: 'bottom top',
        scrub: 1
      },
      opacity: 1,
      y: 0,
      scale: 1
    });

    gsap.to('.hero__wordmark', {
      scrollTrigger: {
        trigger: '.hero',
        start: 'top top',
        end: 'bottom top',
        scrub: 1
      },
      opacity: 0.3,
      y: -100,
      scale: 0.9
    });

    // Problem: Infinite scroll blur effect
    gsap.to('.problem__faces', {
      scrollTrigger: {
        trigger: '.problem',
        start: 'top center',
        end: 'bottom top',
        scrub: 1
      },
      filter: 'blur(12px)',
      opacity: 0.3
    });

    // Intentionality: Moments card 3D rotation
    gsap.from('.intentionality__card', {
      scrollTrigger: {
        trigger: '.intentionality',
        start: 'top 80%',
        end: 'top 30%',
        scrub: 1
      },
      rotateY: -45,
      opacity: 0,
      scale: 0.8
    });

    // ECHO: Constellation nodes animate in
    gsap.from('.echo__node', {
      scrollTrigger: {
        trigger: '.echo',
        start: 'top 70%',
        end: 'top 20%',
        scrub: 1
      },
      opacity: 0,
      scale: 0,
      stagger: 0.05
    });

    // Journey: Trial timer countdown
    gsap.from('.journey__timer', {
      scrollTrigger: {
        trigger: '.journey',
        start: 'top 60%',
        toggleActions: 'play none none reverse'
      },
      textContent: 180,
      duration: 3,
      snap: { textContent: 1 },
      ease: 'none'
    });

    // Privacy: Lock icon glow
    gsap.to('.privacy__lock', {
      scrollTrigger: {
        trigger: '.privacy',
        start: 'top 70%',
        toggleActions: 'play none none reverse'
      },
      boxShadow: '0 0 40px var(--rose-glow)',
      duration: 1,
      ease: 'power2.inOut',
      yoyo: true,
      repeat: -1
    });

    // CTA: Buttons scale up
    gsap.from('.cta__button', {
      scrollTrigger: {
        trigger: '.cta',
        start: 'top 80%',
        end: 'top 50%',
        scrub: 1
      },
      scale: 0.9,
      opacity: 0,
      stagger: 0.1
    });
  }

  scrollToSection(sectionId: string): void {
    if (!this.isBrowser) return;
    const element = document.getElementById(sectionId);
    if (element) {
      element.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }
}
