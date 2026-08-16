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
  selector: 'app-landing-final',
  standalone: true,
  imports: [CommonModule, RouterModule, WovenBgComponent],
  templateUrl: './landing-final.component.html',
  styleUrls: ['./landing-final.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingFinalComponent implements OnInit, AfterViewInit, OnDestroy {
  isBrowser: boolean;
  private lenis?: Lenis;

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
    // Hero: Fade in and scale
    gsap.from('.hero__content', {
      scrollTrigger: {
        trigger: '.hero',
        start: 'top top',
        end: 'bottom top',
        scrub: 1
      },
      opacity: 1,
      scale: 1,
      y: 0
    });

    gsap.to('.hero__content', {
      scrollTrigger: {
        trigger: '.hero',
        start: 'top top',
        end: 'bottom top',
        scrub: 1
      },
      opacity: 0,
      scale: 0.8,
      y: -100
    });

    // Cards: Stagger reveal
    gsap.from('.card', {
      scrollTrigger: {
        trigger: '.cards-section',
        start: 'top 80%',
        end: 'top 20%',
        scrub: 1
      },
      opacity: 0,
      y: 100,
      rotateX: -15,
      stagger: 0.2
    });

    // ECHO nodes: Reveal with depth
    gsap.from('.echo-node', {
      scrollTrigger: {
        trigger: '.echo-section',
        start: 'top 70%',
        end: 'top 20%',
        scrub: 1
      },
      opacity: 0,
      scale: 0,
      stagger: 0.05
    });

    // Text reveals
    gsap.utils.toArray('.reveal-text').forEach((elem: any) => {
      gsap.from(elem, {
        scrollTrigger: {
          trigger: elem,
          start: 'top 85%',
          end: 'top 50%',
          scrub: 1
        },
        opacity: 0,
        y: 50
      });
    });

    // Parallax layers
    gsap.to('.parallax-slow', {
      scrollTrigger: {
        trigger: 'body',
        start: 'top top',
        end: 'bottom bottom',
        scrub: 1
      },
      y: (i, target) => -ScrollTrigger.maxScroll(window) * 0.3
    });

    gsap.to('.parallax-fast', {
      scrollTrigger: {
        trigger: 'body',
        start: 'top top',
        end: 'bottom bottom',
        scrub: 1
      },
      y: (i, target) => -ScrollTrigger.maxScroll(window) * 0.6
    });
  }
}
