import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, PLATFORM_ID, Inject, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { WovenBgComponent } from '../../components/woven-bg/woven-bg.component';
import * as THREE from 'three';
import { gsap } from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
import Lenis from 'lenis';

if (typeof window !== 'undefined') {
  gsap.registerPlugin(ScrollTrigger);
}

interface RegistrationData {
  name: string;
  age: number | null;
  gender: string;
  email: string;
  location: string;
}

@Component({
  selector: 'app-landing-final-real',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, WovenBgComponent],
  templateUrl: './landing-final-real.component.html',
  styleUrls: ['./landing-final-real.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingFinalRealComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('canvas', { static: false }) canvasRef?: ElementRef<HTMLCanvasElement>;

  isBrowser: boolean;

  // Registration form
  registration: RegistrationData = {
    name: '',
    age: null,
    gender: '',
    email: '',
    location: ''
  };
  submitted = false;

  // Moments demo cards with poetic explanations
  momentsCards = [
    {
      name: 'Priya',
      age: 26,
      photo: 'https://images.unsplash.com/photo-1609505848912-b7c3b8b4beda?w=600&h=800&fit=crop',
      explanation: 'You both speak in questions, not answers. Curiosity over certainty.'
    },
    {
      name: 'Arjun',
      age: 28,
      photo: 'https://images.unsplash.com/photo-1506794778202-cad84cf45f1d?w=600&h=800&fit=crop',
      explanation: 'Your answers didn\'t match — they mirrored. Two people asking the same question from different angles.'
    },
    {
      name: 'Meera',
      age: 25,
      photo: 'https://images.unsplash.com/photo-1488426862026-3ee34a7d66df?w=600&h=800&fit=crop',
      explanation: 'You both value presence. Not performance.'
    },
    {
      name: 'Rohan',
      age: 29,
      photo: 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=600&h=800&fit=crop',
      explanation: 'You both chose silence over small talk. Depth over breadth.'
    },
    {
      name: 'Anjali',
      age: 27,
      photo: 'https://images.unsplash.com/photo-1529626455594-4ff0802cfb7e?w=600&h=800&fit=crop',
      explanation: 'Your pillars don\'t align — they complete. What you seek, they give. What they need, you hold.'
    }
  ];

  private scene?: THREE.Scene;
  private camera?: THREE.PerspectiveCamera;
  private renderer?: THREE.WebGLRenderer;
  private cards: THREE.Group[] = [];
  private animationId?: number;
  private lenis?: Lenis;

  constructor(
    @Inject(PLATFORM_ID) private platformId: Object,
    private cdr: ChangeDetectorRef
  ) {
    this.isBrowser = isPlatformBrowser(this.platformId);
  }

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    if (!this.isBrowser) return;

    // Initialize Lenis smooth scroll
    this.lenis = new Lenis({
      duration: 1.5,
      easing: (t) => Math.min(1, 1.001 - Math.pow(2, -10 * t)),
      smoothWheel: true
    });

    const raf = (time: number) => {
      this.lenis?.raf(time);
      requestAnimationFrame(raf);
    };
    requestAnimationFrame(raf);

    // Initialize Three.js for Moments section
    if (this.canvasRef) {
      this.initThreeJS();
      this.animate();
    }

    // Setup scroll animations
    this.setupScrollAnimations();
  }

  ngOnDestroy(): void {
    if (this.animationId) cancelAnimationFrame(this.animationId);
    this.lenis?.destroy();
    this.renderer?.dispose();
    if (this.isBrowser) {
      ScrollTrigger.getAll().forEach(t => t.kill());
    }
  }

  private initThreeJS(): void {
    if (!this.canvasRef) return;
    const canvas = this.canvasRef.nativeElement;

    // Scene
    this.scene = new THREE.Scene();

    // Camera
    this.camera = new THREE.PerspectiveCamera(
      45,
      canvas.clientWidth / canvas.clientHeight,
      0.1,
      100
    );
    this.camera.position.z = 20;

    // Renderer
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    this.renderer.setSize(canvas.clientWidth, canvas.clientHeight);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

    // Lights
    const ambient = new THREE.AmbientLight(0xffffff, 0.6);
    this.scene.add(ambient);

    const light1 = new THREE.PointLight(0xC0392B, 1.5, 40);
    light1.position.set(-8, 5, 5);
    this.scene.add(light1);

    const light2 = new THREE.PointLight(0xD4A017, 1, 40);
    light2.position.set(8, -5, 5);
    this.scene.add(light2);

    // Create polaroid cards
    this.createPolaroidCards();

    // Resize handler
    window.addEventListener('resize', () => this.onResize());
  }

  private createPolaroidCards(): void {
    if (!this.scene) return;

    const spacing = 12;
    this.momentsCards.forEach((card, i) => {
      const loader = new THREE.TextureLoader();
      loader.load(card.photo, (texture) => {
        const group = new THREE.Group();

        // Photo
        const photoGeo = new THREE.PlaneGeometry(3, 4);
        const photoMat = new THREE.MeshStandardMaterial({
          map: texture,
          roughness: 0.8,
          metalness: 0.1
        });
        const photo = new THREE.Mesh(photoGeo, photoMat);
        group.add(photo);

        // Polaroid border
        const borderGeo = new THREE.PlaneGeometry(3.3, 4.6);
        const borderMat = new THREE.MeshStandardMaterial({
          color: 0xf5f5f5,
          roughness: 0.9
        });
        const border = new THREE.Mesh(borderGeo, borderMat);
        border.position.z = -0.01;
        group.add(border);

        // Position in 3D space
        const angle = (i / this.momentsCards.length) * Math.PI * 0.3 - 0.15;
        group.position.set(
          Math.sin(angle) * 4,
          Math.cos(i * 0.5) * 2 - 1,
          -i * spacing
        );
        group.rotation.y = angle * 0.5;

        this.scene!.add(group);
        this.cards.push(group);
      });
    });
  }

  private setupScrollAnimations(): void {
    // Camera dolly through cards
    if (this.camera) {
      gsap.to(this.camera.position, {
        scrollTrigger: {
          trigger: '.moments-demo',
          start: 'top top',
          end: 'bottom bottom',
          scrub: 1
        },
        z: -40,
        ease: 'none'
      });
    }

    // Fade in sections
    gsap.utils.toArray('.fade-in').forEach((elem: any) => {
      gsap.from(elem, {
        scrollTrigger: {
          trigger: elem,
          start: 'top 80%',
          end: 'top 40%',
          scrub: 1
        },
        opacity: 0,
        y: 40
      });
    });
  }

  private animate = (): void => {
    this.animationId = requestAnimationFrame(this.animate);

    // Gentle card rotation
    this.cards.forEach((card, i) => {
      card.rotation.y += 0.001 * (i % 2 === 0 ? 1 : -1);
    });

    if (this.renderer && this.scene && this.camera) {
      this.renderer.render(this.scene, this.camera);
    }
  };

  private onResize(): void {
    if (!this.canvasRef || !this.camera || !this.renderer) return;
    const canvas = this.canvasRef.nativeElement;
    this.camera.aspect = canvas.clientWidth / canvas.clientHeight;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(canvas.clientWidth, canvas.clientHeight);
  }

  onSubmitRegistration(): void {
    if (this.registration.name && this.registration.email && this.registration.age && this.registration.gender && this.registration.location) {
      console.log('Registration:', this.registration);
      this.submitted = true;
      this.cdr.markForCheck();

      // TODO: Send to backend
    }
  }
}
