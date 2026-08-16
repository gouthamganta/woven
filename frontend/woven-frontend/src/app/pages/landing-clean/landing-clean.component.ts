import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, PLATFORM_ID, Inject, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { WovenBgComponent } from '../../components/woven-bg/woven-bg.component';
import * as THREE from 'three';
import { gsap } from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';

if (typeof window !== 'undefined') {
  gsap.registerPlugin(ScrollTrigger);
}

interface MomentsCard {
  name: string;
  age: number;
  photo: string;
  explanation: string;
}

interface RegistrationData {
  name: string;
  age: number | null;
  gender: string;
  email: string;
  location: string;
}

@Component({
  selector: 'app-landing-clean',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, WovenBgComponent],
  templateUrl: './landing-clean.component.html',
  styleUrls: ['./landing-clean.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingCleanComponent implements OnInit, AfterViewInit, OnDestroy {
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

  // Moments demo cards - INDIAN MODELS
  momentsCards: MomentsCard[] = [
    {
      name: 'Priya',
      age: 26,
      photo: 'https://images.unsplash.com/photo-1617127365659-c47fa864d8bc?w=400&h=500&fit=crop&q=80', // Indian woman
      explanation: 'You both speak in questions, not answers. Curiosity over certainty.'
    },
    {
      name: 'Arjun',
      age: 28,
      photo: 'https://images.unsplash.com/photo-1618641986557-1ecd230959aa?w=400&h=500&fit=crop&q=80', // Indian man
      explanation: 'Your answers didn\'t match — they mirrored. Two people asking the same question from different angles.'
    },
    {
      name: 'Meera',
      age: 25,
      photo: 'https://images.unsplash.com/photo-1614624532983-4ce03382d63d?w=400&h=500&fit=crop&q=80', // Indian woman
      explanation: 'You both value presence. Not performance.'
    },
    {
      name: 'Rohan',
      age: 29,
      photo: 'https://images.unsplash.com/photo-1619194617062-5a83b8580edl?w=400&h=500&fit=crop&q=80', // Indian man
      explanation: 'You both chose silence over small talk. Depth over breadth.'
    },
    {
      name: 'Anjali',
      age: 27,
      photo: 'https://images.unsplash.com/photo-1602750120662-9dc25c3d5c5a?w=400&h=500&fit=crop&q=80', // Indian woman
      explanation: 'Your pillars don\'t align — they complete. What you seek, they give. What they need, you hold.'
    }
  ];

  private scene?: THREE.Scene;
  private camera?: THREE.PerspectiveCamera;
  private renderer?: THREE.WebGLRenderer;
  private cards: THREE.Mesh[] = [];
  private animationId?: number;

  constructor(
    @Inject(PLATFORM_ID) private platformId: Object,
    private cdr: ChangeDetectorRef
  ) {
    this.isBrowser = isPlatformBrowser(this.platformId);
  }

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    if (!this.isBrowser || !this.canvasRef) return;

    this.initThreeJS();
    this.setupScrollAnimations();
    this.animate();
  }

  ngOnDestroy(): void {
    if (this.animationId) cancelAnimationFrame(this.animationId);
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
      50,
      canvas.clientWidth / canvas.clientHeight,
      0.1,
      100
    );
    this.camera.position.set(0, 0, 30);

    // Renderer
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    this.renderer.setSize(canvas.clientWidth, canvas.clientHeight);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

    // Lights
    const ambient = new THREE.AmbientLight(0xffffff, 0.8);
    this.scene.add(ambient);

    const light1 = new THREE.PointLight(0xC0392B, 2, 50);
    light1.position.set(-10, 10, 10);
    this.scene.add(light1);

    const light2 = new THREE.PointLight(0xD4A017, 1.5, 50);
    light2.position.set(10, -10, 10);
    this.scene.add(light2);

    // Create polaroid cards in STRAIGHT LINE (clean, not random)
    this.createPolaroidCards();

    // Resize handler
    window.addEventListener('resize', () => this.onResize());
  }

  private createPolaroidCards(): void {
    if (!this.scene) return;

    const spacing = 15; // Distance between cards
    const startZ = -10; // Start position

    this.momentsCards.forEach((card, i) => {
      const loader = new THREE.TextureLoader();
      loader.load(card.photo, (texture) => {
        // Polaroid card group
        const group = new THREE.Group();

        // Photo (3:4 ratio)
        const photoGeo = new THREE.PlaneGeometry(4, 5);
        const photoMat = new THREE.MeshStandardMaterial({
          map: texture,
          roughness: 0.7,
          metalness: 0.1
        });
        const photo = new THREE.Mesh(photoGeo, photoMat);
        group.add(photo);

        // White polaroid border
        const borderGeo = new THREE.PlaneGeometry(4.4, 5.8);
        const borderMat = new THREE.MeshStandardMaterial({
          color: 0xffffff,
          roughness: 0.9
        });
        const border = new THREE.Mesh(borderGeo, borderMat);
        border.position.z = -0.01;
        group.add(border);

        // Position cards in CLEAN STRAIGHT LINE along Z-axis
        group.position.set(
          0, // Centered horizontally
          0, // Centered vertically
          startZ - (i * spacing) // Evenly spaced along Z
        );

        // Small rotation for visual interest (NOT random)
        const rotationPattern = [0.05, -0.05, 0.03, -0.04, 0.02];
        group.rotation.y = rotationPattern[i % rotationPattern.length];

        this.scene!.add(group);
        this.cards.push(photo); // Store for animation
      });
    });
  }

  private setupScrollAnimations(): void {
    if (!this.camera) return;

    // Camera dollies THROUGH the cards (straight line movement)
    gsap.to(this.camera.position, {
      scrollTrigger: {
        trigger: '.moments-3d-section',
        start: 'top top',
        end: 'bottom bottom',
        scrub: 1
      },
      z: -50, // Move camera forward through cards
      ease: 'none'
    });

    // Fade in text sections
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

    // Gentle rotation on cards
    this.cards.forEach((card, i) => {
      if (card.parent) {
        card.parent.rotation.y += 0.001 * (i % 2 === 0 ? 1 : -1);
      }
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
    }
  }
}
