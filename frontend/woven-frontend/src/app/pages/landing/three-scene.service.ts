import { Injectable, NgZone } from '@angular/core';
import * as THREE from 'three';

@Injectable({ providedIn: 'root' })
export class ThreeSceneService {
  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;
  private renderer!: THREE.WebGLRenderer;
  private animationFrameId?: number;

  constructor(private ngZone: NgZone) {}

  initScene(canvas: HTMLCanvasElement): void {
    // Scene
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x0E0912); // --bg-base

    // Camera
    this.camera = new THREE.PerspectiveCamera(
      75,
      window.innerWidth / window.innerHeight,
      0.1,
      1000
    );
    this.camera.position.z = 5;

    // Renderer
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    this.renderer.setSize(window.innerWidth, window.innerHeight);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

    // Lighting
    const ambientLight = new THREE.AmbientLight(0xffffff, 0.5);
    this.scene.add(ambientLight);

    const pointLight1 = new THREE.PointLight(0xE8564A, 1, 100); // crimson
    pointLight1.position.set(5, 5, 5);
    this.scene.add(pointLight1);

    const pointLight2 = new THREE.PointLight(0xD4A850, 0.8, 100); // gold
    pointLight2.position.set(-5, -5, 5);
    this.scene.add(pointLight2);

    // Handle resize
    window.addEventListener('resize', () => this.onResize());

    // Start render loop
    this.ngZone.runOutsideAngular(() => this.animate());
  }

  createFloatingCard(
    text: string,
    position: { x: number; y: number; z: number },
    rotation: { x: number; y: number; z: number },
    color: number = 0x1E1228
  ): THREE.Mesh {
    // Card geometry (plane)
    const geometry = new THREE.PlaneGeometry(2, 2.8);
    const material = new THREE.MeshStandardMaterial({
      color,
      roughness: 0.7,
      metalness: 0.3,
      side: THREE.DoubleSide
    });
    const card = new THREE.Mesh(geometry, material);
    card.position.set(position.x, position.y, position.z);
    card.rotation.set(rotation.x, rotation.y, rotation.z);

    // Add border (edges)
    const edges = new THREE.EdgesGeometry(geometry);
    const lineMaterial = new THREE.LineBasicMaterial({ color: 0xE8564A, linewidth: 2 });
    const border = new THREE.LineSegments(edges, lineMaterial);
    card.add(border);

    this.scene.add(card);
    return card;
  }

  createSphere(
    position: { x: number; y: number; z: number },
    radius: number = 0.2,
    color: number = 0xE8564A
  ): THREE.Mesh {
    const geometry = new THREE.SphereGeometry(radius, 32, 32);
    const material = new THREE.MeshStandardMaterial({
      color,
      emissive: color,
      emissiveIntensity: 0.5,
      roughness: 0.3,
      metalness: 0.7
    });
    const sphere = new THREE.Mesh(geometry, material);
    sphere.position.set(position.x, position.y, position.z);
    this.scene.add(sphere);
    return sphere;
  }

  createConnectionLine(
    start: THREE.Vector3,
    end: THREE.Vector3,
    color: number = 0xE8564A
  ): THREE.Line {
    const points = [start, end];
    const geometry = new THREE.BufferGeometry().setFromPoints(points);
    const material = new THREE.LineBasicMaterial({ color, opacity: 0.3, transparent: true });
    const line = new THREE.Line(geometry, material);
    this.scene.add(line);
    return line;
  }

  createTextMesh(
    text: string,
    position: { x: number; y: number; z: number },
    size: number = 1
  ): THREE.Mesh {
    // Simple placeholder box for text (real text requires TextGeometry + font loader)
    const geometry = new THREE.BoxGeometry(size, size * 0.3, 0.1);
    const material = new THREE.MeshStandardMaterial({
      color: 0x9E99E8, // plum
      emissive: 0x9E99E8,
      emissiveIntensity: 0.3
    });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.position.set(position.x, position.y, position.z);
    this.scene.add(mesh);
    return mesh;
  }

  updateCameraOnScroll(scrollProgress: number): void {
    // scrollProgress: 0 (top) → 1 (bottom)
    // Move camera down and forward as user scrolls
    this.camera.position.y = 5 - scrollProgress * 15; // Move down
    this.camera.position.z = 5 - scrollProgress * 3;  // Move forward
    this.camera.lookAt(0, -scrollProgress * 10, 0);
  }

  rotateObject(object: THREE.Object3D, delta: number): void {
    object.rotation.y += delta * 0.5;
    object.rotation.x += delta * 0.2;
  }

  private animate = (): void => {
    this.animationFrameId = requestAnimationFrame(this.animate);
    this.renderer.render(this.scene, this.camera);
  };

  private onResize(): void {
    this.camera.aspect = window.innerWidth / window.innerHeight;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(window.innerWidth, window.innerHeight);
  }

  destroy(): void {
    if (this.animationFrameId) {
      cancelAnimationFrame(this.animationFrameId);
    }
    window.removeEventListener('resize', () => this.onResize());
    this.renderer.dispose();
  }

  getScene(): THREE.Scene {
    return this.scene;
  }

  getCamera(): THREE.PerspectiveCamera {
    return this.camera;
  }
}
