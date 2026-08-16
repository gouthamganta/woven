import { Component, AfterViewInit, OnDestroy, ViewChild, ElementRef, PLATFORM_ID, Inject, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { WovenBgComponent } from '../../components/woven-bg/woven-bg.component';

interface RegistrationData {
  name: string;
  age: number | null;
  gender: string;
  email: string;
  location: string;
}

@Component({
  selector: 'app-landing-simple',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, WovenBgComponent],
  templateUrl: './landing-simple.component.html',
  styleUrls: ['./landing-simple.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingSimpleComponent implements AfterViewInit, OnDestroy {
  @ViewChild('introEl') introEl?: ElementRef<HTMLDivElement>;
  @ViewChild('introVideo') videoRef?: ElementRef<HTMLVideoElement>;

  isBrowser: boolean;
  showIntro = true;
  showTapPrompt = false;
  isMobileDevice = false;
  private hideIntroTimer?: ReturnType<typeof setTimeout>;
  private videoStartAttempted = false;
  introVideoSrc: string = '/login-intro.mp4'; // Default to desktop video

  registration: RegistrationData = {
    name: '',
    age: null,
    gender: '',
    email: '',
    location: ''
  };
  submitted = false;

  // Moments people with match explanations (ALL 7 photos)
  people = [
    {
      photo: '/landing-photos/Ananya.jpeg',
      name: 'Ananya',
      age: 26,
      explanation: 'You both speak in questions, not answers. Curiosity over certainty.'
    },
    {
      photo: '/landing-photos/Laleh.jpeg',
      name: 'Laleh',
      age: 25,
      explanation: 'Your answers didn\'t match — they mirrored. Two people asking the same question from different angles.'
    },
    {
      photo: '/landing-photos/Mimi.jpeg',
      name: 'Mimi',
      age: 27,
      explanation: 'You both value presence. Not performance.'
    },
    {
      photo: '/landing-photos/Screenshot 2026-07-04 174543.png',
      name: 'Rohan',
      age: 28,
      explanation: 'You both chose silence over small talk. Depth over breadth.'
    },
    {
      photo: '/landing-photos/zara..charater.jpeg',
      name: 'Zara',
      age: 26,
      explanation: 'Your pillars don\'t align — they complete. What you seek, they give. What they need, you hold.'
    },
    {
      photo: '/landing-photos/Screenshot 2026-07-04 174554.png',
      name: 'Arjun',
      age: 29,
      explanation: 'You both lead with honesty, even when it costs you. Integrity over ease.'
    },
    {
      photo: '/landing-photos/ChatGPT Image Jul 4, 2026, 05_45_05 PM.png',
      name: 'Krishna',
      age: 27,
      explanation: 'Your energy matches. Not loud, not quiet — just the same frequency.'
    }
  ];

  constructor(
    @Inject(PLATFORM_ID) platformId: Object,
    private cdr: ChangeDetectorRef
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  ngAfterViewInit(): void {
    if (!this.isBrowser) return;

    // Detect mobile and set video source
    this.isMobileDevice = window.innerWidth <= 768;
    this.introVideoSrc = this.isMobileDevice ? '/login-intro-mobile.mp4' : '/login-intro.mp4';
    this.showTapPrompt = this.isMobileDevice;
    console.log('[Landing] AfterViewInit - Device:', this.isMobileDevice ? 'mobile' : 'desktop', 'showTapPrompt:', this.showTapPrompt);
    this.cdr.markForCheck();

    // Fallback: hide intro if video never fires 'ended'
    this.hideIntroTimer = setTimeout(() => this.hideLandingIntro(), 13_000);

    // Delay slightly for SSR hydration
    setTimeout(() => this.startVideo(), 200);
  }

  private initScrollAnimations(): void {
    if (!this.isBrowser) return;

    // Dynamically import GSAP to avoid SSR issues
    import('gsap').then(({ gsap }) => {
      import('gsap/ScrollTrigger').then(({ ScrollTrigger }) => {
        gsap.registerPlugin(ScrollTrigger);

        // Hero parallax
        this.initHeroParallax(gsap, ScrollTrigger);

        // Polaroid cards stagger reveal
        this.initPolaroidReveal(gsap, ScrollTrigger);

        // Commons stagger reveal
        this.initCommonsReveal(gsap, ScrollTrigger);
      });
    });
  }

  private initCarousels(): void {
    if (!this.isBrowser) return;

    import('gsap').then(({ gsap }) => {
      import('gsap/Draggable').then(({ Draggable }) => {
        gsap.registerPlugin(Draggable);

        // Initialize carousels
        this.initPolaroidCarousel(gsap, Draggable);
        this.initAboutCarousel(gsap, Draggable);
      });
    });
  }

  private initPolaroidCarousel(gsap: any, Draggable: any): void {
    const carousel = document.querySelector('.polaroid-carousel');
    const track = document.querySelector('.carousel-track') as HTMLElement;
    const cards = gsap.utils.toArray('.polaroid');

    if (!carousel || !track || cards.length === 0) return;

    const cardWidth = 420; // card width + gap (360px + 60px)
    const startIndex = 2; // Start at card 3 (index 2)
    let currentX = -(cardWidth * startIndex);
    const maxX = 0;
    const minX = -(cardWidth * (cards.length - 1));

    Draggable.create(track, {
      type: 'x',
      inertia: true,
      bounds: { minX, maxX },
      onDrag: updateCarousel,
      onThrowUpdate: updateCarousel,
      snap: {
        x: (endValue: number) => {
          return Math.round(endValue / cardWidth) * cardWidth;
        }
      }
    });

    function updateCarousel() {
      currentX = gsap.getProperty(track, 'x') as number;
      const scrollProgress = Math.abs(currentX) / cardWidth;
      const centerIndex = scrollProgress;

      cards.forEach((card: any, i: number) => {
        const distance = Math.abs(i - centerIndex);
        const scale = Math.max(0.7, 1 - distance * 0.15);
        const opacity = Math.max(0.5, 1 - distance * 0.25);
        const rotateY = (i - centerIndex) * 8;

        gsap.set(card, {
          scale,
          opacity,
          rotateY,
          z: distance * -50
        });

        if (distance < 0.5) {
          card.classList.add('center-card');
        } else {
          card.classList.remove('center-card');
        }
      });
    }

    gsap.set(track, { x: currentX });
    updateCarousel();
  }

  private initAboutCarousel(gsap: any, Draggable: any): void {
    const track = document.querySelector('.about-track') as HTMLElement;
    const slides = gsap.utils.toArray('.about-slide');

    if (!track || slides.length === 0) return;

    const slideWidth = 450;
    const gap = 40;
    const totalWidth = slideWidth + gap;
    const startIndex = 1;

    // Set initial position (middle slide)
    gsap.set(track, { x: -(totalWidth * startIndex) });

    Draggable.create(track, {
      type: 'x',
      inertia: true,
      bounds: {
        minX: -(totalWidth * (slides.length - 1)),
        maxX: 0
      },
      snap: {
        x: (endValue: number) => Math.round(endValue / totalWidth) * totalWidth
      }
    });
  }


  private initHeroParallax(gsap: any, ScrollTrigger: any): void {
    const heroVisual = document.querySelector('.hero-visual');
    if (!heroVisual) return;

    gsap.to(heroVisual, {
      y: 150,
      opacity: 0.3,
      scrollTrigger: {
        trigger: '.hero',
        start: "top top",
        end: "bottom top",
        scrub: true
      }
    });
  }

  private initPolaroidReveal(gsap: any, ScrollTrigger: any): void {
    const polaroids = gsap.utils.toArray('.polaroid');

    polaroids.forEach((item: any, i: number) => {
      gsap.fromTo(item,
        {
          opacity: 0,
          scale: 0.8,
          y: 40
        },
        {
          opacity: 1,
          scale: 1,
          y: 0,
          duration: 0.6,
          scrollTrigger: {
            trigger: '.polaroid-carousel',
            start: "top 85%",
            end: "bottom 15%",
            toggleActions: "play reverse play reverse"
          },
          delay: i * 0.1
        }
      );
    });
  }

  private initCommonsReveal(gsap: any, ScrollTrigger: any): void {
    const gridItems = gsap.utils.toArray('.commons-grid .grid-item');

    gridItems.forEach((item: any, i: number) => {
      gsap.fromTo(item,
        {
          opacity: 0,
          scale: 0.8,
          y: 40
        },
        {
          opacity: 1,
          scale: 1,
          y: 0,
          duration: 0.6,
          scrollTrigger: {
            trigger: item,
            start: "top 85%",
            end: "bottom 15%",
            toggleActions: "play reverse play reverse"
          },
          delay: (i % 4) * 0.1
        }
      );
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.hideIntroTimer);
  }

  private startVideo(): void {
    const video = this.videoRef?.nativeElement;
    if (!video) {
      this.hideLandingIntro();
      return;
    }

    video.muted = true;
    video.currentTime = 0;

    // On mobile, wait for user tap (don't try autoplay)
    if (this.isMobileDevice) {
      console.log('[Landing] Mobile detected - waiting for user tap');
      return;
    }

    // Desktop: try autoplay
    const tryPlay = () => {
      video.play().then(() => {
        this.videoStartAttempted = true;
        this.showTapPrompt = false;
        this.cdr.markForCheck();
      }).catch((err: any) => {
        console.warn('[Landing] video.play() blocked - showing tap prompt:', err);
        this.showTapPrompt = true;
        this.videoStartAttempted = false;
        this.cdr.markForCheck();
      });
    };

    if (video.readyState >= 3) {
      tryPlay();
    } else {
      video.addEventListener('canplay', tryPlay, { once: true });
    }
  }

  onIntroClick(): void {
    if (this.showTapPrompt && !this.videoStartAttempted) {
      const video = this.videoRef?.nativeElement;
      if (video) {
        video.play().then(() => {
          this.showTapPrompt = false;
          this.videoStartAttempted = true;
          this.cdr.markForCheck();
        }).catch(err => console.warn('[Landing] Manual play failed:', err));
      }
    }
  }

  onVideoEnd(): void {
    clearTimeout(this.hideIntroTimer);
    this.hideLandingIntro();
  }

  skipIntro(): void {
    clearTimeout(this.hideIntroTimer);
    this.hideLandingIntro();
  }

  private hideLandingIntro(): void {
    this.showIntro = false;
    this.cdr.markForCheck();

    // Initialize carousels and animations after DOM updates
    setTimeout(() => {
      this.initScrollAnimations();
      this.initCarousels();
    }, 100);
  }

  onSubmitRegistration(): void {
    if (this.registration.name && this.registration.email && this.registration.age && this.registration.gender && this.registration.location) {
      console.log('Registration:', this.registration);
      this.submitted = true;
      this.cdr.markForCheck();
    }
  }
}
