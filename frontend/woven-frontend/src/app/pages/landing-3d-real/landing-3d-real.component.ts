import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, PLATFORM_ID, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { RouterModule } from '@angular/router';
import * as THREE from 'three';
import Lenis from 'lenis';

@Component({
  selector: 'app-landing-3d-real',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './landing-3d-real.component.html',
  styleUrls: ['./landing-3d-real.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Landing3DRealComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('canvas', { static: false }) canvasRef!: ElementRef<HTMLCanvasElement>;

  isBrowser: boolean;
  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;
  private renderer!: THREE.WebGLRenderer;
  private cards: THREE.Group[] = [];
  private particles: THREE.Points[] = [];
  private animationId?: number;
  private lenis?: Lenis;

  // Demo photos (Unsplash)
  private photoUrls = [
    'https://images.unsplash.com/photo-1494790108377-be9c29b29330?w=600&h=800&fit=crop',
    'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=600&h=800&fit=crop',
    'https://images.unsplash.com/photo-1524504388940-b1c1722653e1?w=600&h=800&fit=crop',
    'https://images.unsplash.com/photo-1517841905240-472988babdf9?w=600&h=800&fit=crop',
    'https://images.unsplash.com/photo-1529626455594-4ff0802cfb7e?w=600&h=800&fit=crop'
  ];

  constructor(@Inject(PLATFORM_ID) private platformId: Object) {
    this.isBrowser = isPlatformBrowser(this.platformId);
  }

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    if (!this.isBrowser || !this.canvasRef) return;

    this.initThreeJS();
    this.initLenis();
    this.animate();

    window.addEventListener('scroll', () => this.onScroll(), { passive: true });
    window.addEventListener('resize', () => this.onResize());
  }

  ngOnDestroy(): void {
    if (this.animationId) cancelAnimationFrame(this.animationId);
    this.lenis?.destroy();
    this.renderer?.dispose();
    window.removeEventListener('scroll', () => this.onScroll());
    window.removeEventListener('resize', () => this.onResize());
  }

  private initThreeJS(): void {
    const canvas = this.canvasRef.nativeElement;

    // Scene
    this.scene = new THREE.Scene();
    this.scene.fog = new THREE.Fog(0x0E0912, 20, 100);

    // Camera
    this.camera = new THREE.PerspectiveCamera(
      50,
      window.innerWidth / window.innerHeight,
      0.1,
      200
    );
    this.camera.position.z = 15;

    // Renderer
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    this.renderer.setSize(window.innerWidth, window.innerHeight);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

    // Lighting
    const ambient = new THREE.AmbientLight(0xffffff, 0.4);
    this.scene.add(ambient);

    const crimsonLight = new THREE.PointLight(0xC0392B, 2, 50);
    crimsonLight.position.set(-10, 10, 5);
    this.scene.add(crimsonLight);

    const goldLight = new THREE.PointLight(0xD4A017, 1.5, 50);
    goldLight.position.set(10, -10, 10);
    this.scene.add(goldLight);

    // Create polaroid cards
    this.createPolaroidCards();

    // Create particle system
    this.createParticles();
  }

  private createPolaroidCards(): void {
    const cardPositions = [
      { x: -3, y: 2, z: -15, rotY: 0.2 },
      { x: 3, y: -1, z: -25, rotY: -0.3 },
      { x: -2, y: -3, z: -35, rotY: 0.15 },
      { x: 4, y: 1, z: -45, rotY: -0.2 },
      { x: -4, y: -2, z: -55, rotY: 0.25 }
    ];

    cardPositions.forEach((pos, i) => {
      // Load texture
      const loader = new THREE.TextureLoader();
      loader.load(this.photoUrls[i], (texture) => {
        // Card geometry (3:4 ratio polaroid)
        const cardGroup = new THREE.Group();

        // Photo plane
        const photoGeo = new THREE.PlaneGeometry(3, 4);
        const photoMat = new THREE.MeshStandardMaterial({
          map: texture,
          roughness: 0.8,
          metalness: 0.1
        });
        const photo = new THREE.Mesh(photoGeo, photoMat);
        cardGroup.add(photo);

        // White border (polaroid frame)
        const borderGeo = new THREE.PlaneGeometry(3.3, 4.5);
        const borderMat = new THREE.MeshStandardMaterial({
          color: 0xf5f5f5,
          roughness: 0.9,
          metalness: 0
        });
        const border = new THREE.Mesh(borderGeo, borderMat);
        border.position.z = -0.01;
        cardGroup.add(border);

        // Shadow plane
        const shadowGeo = new THREE.PlaneGeometry(3.5, 4.7);
        const shadowMat = new THREE.ShadowMaterial({ opacity: 0.3 });
        const shadow = new THREE.Mesh(shadowGeo, shadowMat);
        shadow.position.z = -0.02;
        cardGroup.add(shadow);

        // Position card
        cardGroup.position.set(pos.x, pos.y, pos.z);
        cardGroup.rotation.y = pos.rotY;

        this.scene.add(cardGroup);
        this.cards.push(cardGroup);
      });
    });
  }

  private createParticles(): void {
    const particleCount = 100;
    const geometry = new THREE.BufferGeometry();
    const positions = new Float32Array(particleCount * 3);

    for (let i = 0; i < particleCount; i++) {
      positions[i * 3] = (Math.random() - 0.5) * 50;
      positions[i * 3 + 1] = (Math.random() - 0.5) * 50;
      positions[i * 3 + 2] = (Math.random() - 0.5) * 80 - 20;
    }

    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));

    const material = new THREE.PointsMaterial({
      color: 0x7F77DD,
      size: 0.15,
      transparent: true,
      opacity: 0.6,
      sizeAttenuation: true
    });

    const particles = new THREE.Points(geometry, material);
    this.scene.add(particles);
    this.particles.push(particles);
  }

  private initLenis(): void {
    this.lenis = new Lenis({
      duration: 1.5,
      easing: (t) => Math.min(1, 1.001 - Math.pow(2, -10 * t)),
      smoothWheel: true
    });
  }

  private animate = (): void => {
    this.animationId = requestAnimationFrame(this.animate);

    // Update Lenis
    this.lenis?.raf(Date.now());

    // Gentle rotation on cards
    this.cards.forEach((card, i) => {
      card.rotation.y += 0.001 * (i % 2 === 0 ? 1 : -1);
      card.rotation.x += 0.0005;
    });

    // Particle drift
    this.particles.forEach(p => {
      p.rotation.y += 0.0002;
    });

    this.renderer.render(this.scene, this.camera);
  };

  private onScroll(): void {
    const scrollY = window.scrollY;
    const maxScroll = document.body.scrollHeight - window.innerHeight;
    const scrollProgress = scrollY / maxScroll;

    // Move camera forward as user scrolls
    this.camera.position.z = 15 - scrollProgress * 70;
    this.camera.position.y = -scrollProgress * 10;
    this.camera.lookAt(0, -scrollProgress * 8, -40);
  }

  private onResize(): void {
    this.camera.aspect = window.innerWidth / window.innerHeight;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(window.innerWidth, window.innerHeight);
  }
}
