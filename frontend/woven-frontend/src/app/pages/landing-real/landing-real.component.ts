import { Component, OnInit, OnDestroy, AfterViewInit, PLATFORM_ID, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { RouterModule } from '@angular/router';
import { WovenBgComponent } from '../../components/woven-bg/woven-bg.component';
import { gsap } from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
import Lenis from 'lenis';

if (typeof window !== 'undefined') {
  gsap.registerPlugin(ScrollTrigger);
}

@Component({
  selector: 'app-landing-real',
  standalone: true,
  imports: [CommonModule, RouterModule, WovenBgComponent],
  templateUrl: './landing-real.component.html',
  styleUrls: ['./landing-real.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingRealComponent implements OnInit, AfterViewInit, OnDestroy {
  isBrowser: boolean;
  private lenis?: Lenis;

  // Demo Moments cards
  demoCards = [
    {
      name: 'Sarah',
      verified: true,
      photo: 'https://images.unsplash.com/photo-1494790108377-be9c29b29330?w=800&h=1000&fit=crop',
      explanation: 'You both value deep conversation over small talk. Shared curiosity about philosophy and art.',
      choice: 'magical' as const
    },
    {
      name: 'Maya',
      verified: true,
      photo: 'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=800&h=1000&fit=crop',
      explanation: 'Similar energy around intentional living. Both mentioned wanting presence over perfection.',
      choice: 'resonant' as const
    },
    {
      name: 'Priya',
      verified: false,
      photo: 'https://images.unsplash.com/photo-1524504388940-b1c1722653e1?w=800&h=1000&fit=crop',
      explanation: 'You answered foundational questions in complementary ways. Strong pillar alignment.',
      choice: 'magical' as const
    }
  ];

  constructor(@Inject(PLATFORM_ID) private platformId: Object) {
    this.isBrowser = isPlatformBrowser(this.platformId);
  }

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    if (!this.isBrowser) return;

    // Initialize Lenis smooth scroll
    this.lenis = new Lenis({
      duration: 1.2,
      easing: (t) => Math.min(1, 1.001 - Math.pow(2, -10 * t)),
      smoothWheel: true
    });

    const raf = (time: number) => {
      this.lenis?.raf(time);
      requestAnimationFrame(raf);
    };
    requestAnimationFrame(raf);

    // Initialize scroll animations
    requestAnimationFrame(() => this.initAnimations());
  }

  ngOnDestroy(): void {
    this.lenis?.destroy();
    if (this.isBrowser) {
      ScrollTrigger.getAll().forEach(trigger => trigger.kill());
    }
  }

  private initAnimations(): void {
    // Hero fade
    gsap.to('.hero-content', {
      scrollTrigger: {
        trigger: '.hero',
        start: 'top top',
        end: 'bottom top',
        scrub: 1
      },
      opacity: 0,
      y: -50
    });

    // Cards stagger
    gsap.from('.demo-card', {
      scrollTrigger: {
        trigger: '.cards-demo',
        start: 'top 75%',
        end: 'top 25%',
        scrub: 1
      },
      opacity: 0,
      y: 80,
      stagger: 0.15
    });

    // Feature sections
    gsap.utils.toArray('.feature-section').forEach((section: any) => {
      gsap.from(section, {
        scrollTrigger: {
          trigger: section,
          start: 'top 80%',
          end: 'top 40%',
          scrub: 1
        },
        opacity: 0,
        y: 40
      });
    });
  }
}
