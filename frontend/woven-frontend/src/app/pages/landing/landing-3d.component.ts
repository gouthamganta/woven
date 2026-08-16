import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, PLATFORM_ID, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { RouterModule } from '@angular/router';
import { ThreeSceneService } from './three-scene.service';
import * as THREE from 'three';

@Component({
  selector: 'app-landing-3d',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './landing-3d.component.html',
  styleUrls: ['./landing-3d.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [ThreeSceneService]
})
export class Landing3DComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('threeCanvas', { static: false }) canvasRef!: ElementRef<HTMLCanvasElement>;

  isBrowser: boolean;
  private cards: THREE.Mesh[] = [];
  private spheres: THREE.Mesh[] = [];
  private scrollListener?: () => void;

  constructor(
    @Inject(PLATFORM_ID) private platformId: Object,
    private threeScene: ThreeSceneService
  ) {
    this.isBrowser = isPlatformBrowser(this.platformId);
  }

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    if (!this.isBrowser || !this.canvasRef) return;

    // Initialize Three.js scene
    this.threeScene.initScene(this.canvasRef.nativeElement);

    // Create 3D elements
    this.createHeroElements();
    this.createEchoConstellation();
    this.createMomentsCardStack();

    // Wire scroll listener
    this.scrollListener = () => this.onScroll();
    window.addEventListener('scroll', this.scrollListener, { passive: true });
  }

  ngOnDestroy(): void {
    if (this.scrollListener) {
      window.removeEventListener('scroll', this.scrollListener);
    }
    this.threeScene.destroy();
  }

  private createHeroElements(): void {
    // WOVEN wordmark as 3D text placeholder (box for now)
    const wordmark = this.threeScene.createTextMesh('WOVEN', { x: 0, y: 4, z: 0 }, 3);

    // Floating accent spheres around wordmark
    this.spheres.push(
      this.threeScene.createSphere({ x: -3, y: 5, z: -2 }, 0.3, 0xE8564A), // crimson
      this.threeScene.createSphere({ x: 3, y: 4.5, z: -1.5 }, 0.25, 0xD4A850), // gold
      this.threeScene.createSphere({ x: 0, y: 6, z: -3 }, 0.2, 0x9E99E8)  // plum
    );
  }

  private createEchoConstellation(): void {
    // 16 ECHO nodes as glowing spheres at different depths
    const nodePositions = [
      { x: -4, y: -5, z: -2 },   // Pillar
      { x: 4, y: -4.5, z: -1.5 }, // Voice
      { x: -2, y: -7, z: -3 },    // Lifestyle
      { x: 2, y: -6.5, z: -2.5 }, // Orbit
      { x: 0, y: -8, z: -1 },     // Intent
      { x: -3, y: -9, z: -2 },    // Humor
      { x: 3, y: -9.5, z: -3 },   // Expression
      { x: -1, y: -10.5, z: -1.5 }, // Style
      { x: 1, y: -11, z: -2 },    // Pulse
      { x: -4, y: -12, z: -3 },   // CF
      { x: 4, y: -12.5, z: -2.5 }, // SharedTile
      { x: 0, y: -13.5, z: -1 },  // Preference
      { x: -2, y: -14, z: -2 },   // Emotional
      { x: 2, y: -14.5, z: -3 },  // Attachment
      { x: -3, y: -15.5, z: -1.5 }, // BehavioralLifestyle
      { x: 3, y: -16, z: -2 }     // VisualPreference
    ];

    const colors = [0xE8564A, 0xD4A850, 0x9E99E8]; // Crimson, Gold, Plum

    nodePositions.forEach((pos, i) => {
      const color = colors[i % colors.length];
      const sphere = this.threeScene.createSphere(pos, 0.15, color);
      this.spheres.push(sphere);
    });

    // Create connection lines between nearby nodes
    for (let i = 0; i < nodePositions.length - 1; i++) {
      const start = new THREE.Vector3(nodePositions[i].x, nodePositions[i].y, nodePositions[i].z);
      const end = new THREE.Vector3(nodePositions[i + 1].x, nodePositions[i + 1].y, nodePositions[i + 1].z);
      this.threeScene.createConnectionLine(start, end, 0xE8564A);
    }
  }

  private createMomentsCardStack(): void {
    // Stack of 5 Moments cards floating in 3D space
    const cardOffsets = [
      { x: -1, y: -20, z: 0, rx: 0.1, ry: -0.2, rz: 0.05 },
      { x: 0.5, y: -21, z: -0.5, rx: -0.1, ry: 0.15, rz: -0.03 },
      { x: -0.5, y: -22, z: 0.3, rx: 0.05, ry: -0.1, rz: 0.08 },
      { x: 1, y: -23, z: -0.8, rx: -0.08, ry: 0.2, rz: -0.05 },
      { x: 0, y: -24, z: 0.5, rx: 0.12, ry: -0.15, rz: 0.1 }
    ];

    cardOffsets.forEach((offset, i) => {
      const card = this.threeScene.createFloatingCard(
        `Card ${i + 1}`,
        { x: offset.x, y: offset.y, z: offset.z },
        { x: offset.rx, y: offset.ry, z: offset.rz },
        0x1E1228 // --bg-elevated
      );
      this.cards.push(card);
    });
  }

  private onScroll(): void {
    const scrollTop = window.pageYOffset || document.documentElement.scrollTop;
    const scrollHeight = document.documentElement.scrollHeight - window.innerHeight;
    const scrollProgress = Math.min(scrollTop / scrollHeight, 1);

    // Update camera position based on scroll
    this.threeScene.updateCameraOnScroll(scrollProgress);

    // Gentle rotation on cards and spheres
    const delta = 0.005;
    this.cards.forEach(card => this.threeScene.rotateObject(card, delta));
    this.spheres.forEach(sphere => this.threeScene.rotateObject(sphere, delta * 0.5));
  }

  scrollToSection(sectionId: string): void {
    if (!this.isBrowser) return;
    const element = document.getElementById(sectionId);
    if (element) {
      element.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }
}
