import {
  Component, OnInit, OnDestroy, ElementRef, ViewChild,
  Inject, PLATFORM_ID, ChangeDetectionStrategy,
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

/**
 * Full-screen Three.js particle field — rose/plum/white dots that drift
 * slowly and respond to mouse movement. Sits behind all page content as
 * a persistent ambient layer, replacing the CSS body::before symbol pattern.
 */
@Component({
  selector: 'app-woven-bg',
  standalone: true,
  template: `<canvas #c></canvas>`,
  styles: [`
    :host { display: contents; }
    canvas {
      position: fixed;
      inset: 0;
      width: 100%;
      height: 100%;
      z-index: 0;
      pointer-events: none;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WovenBgComponent implements OnInit, OnDestroy {
  @ViewChild('c', { static: true }) canvasRef!: ElementRef<HTMLCanvasElement>;

  private animId = 0;
  private t = 0;
  private mouseX = 0;
  private mouseY = 0;
  private renderer: any;
  private scene: any;
  private camera: any;
  private points: any;
  private prefersReduced = false;

  constructor(@Inject(PLATFORM_ID) private pid: object) {}

  ngOnInit() {
    if (!isPlatformBrowser(this.pid)) return;
    this.prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    this.boot();
  }

  ngOnDestroy() {
    if (!isPlatformBrowser(this.pid)) return;
    cancelAnimationFrame(this.animId);
    window.removeEventListener('mousemove', this.onMouse);
    window.removeEventListener('resize', this.onResize);
    this.renderer?.dispose();
  }

  private async boot() {
    const THREE = await import('three');
    const canvas = this.canvasRef.nativeElement;
    const W = window.innerWidth;
    const H = window.innerHeight;

    this.renderer = new THREE.WebGLRenderer({
      canvas,
      alpha: true,
      antialias: false,
      powerPreference: 'low-power',
    });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.5));
    this.renderer.setSize(W, H);
    this.renderer.setClearColor(0x000000, 0);

    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(60, W / H, 0.1, 100);
    this.camera.position.z = 6;

    // Rose, plum, and near-white palette — matches the dark plum design system
    const palette = [
      [0.878, 0.329, 0.565], // rose-400  #E05490
      [0.490, 0.357, 0.816], // plum-400  #7D5BD0
      [0.941, 0.478, 0.675], // rose-300  #F07AAC
      [0.627, 0.498, 0.847], // plum-300  #A07FD8
      [0.996, 0.957, 0.980], // near-white
    ];

    const COUNT = 200;
    const pos = new Float32Array(COUNT * 3);
    const col = new Float32Array(COUNT * 3);

    for (let i = 0; i < COUNT; i++) {
      const i3 = i * 3;
      pos[i3]     = (Math.random() - 0.5) * 24;
      pos[i3 + 1] = (Math.random() - 0.5) * 15;
      pos[i3 + 2] = (Math.random() - 0.5) * 8;
      const c = palette[Math.floor(Math.random() * palette.length)];
      col[i3]     = c[0];
      col[i3 + 1] = c[1];
      col[i3 + 2] = c[2];
    }

    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('color',    new THREE.BufferAttribute(col, 3));

    const mat = new THREE.PointsMaterial({
      size: 0.052,
      vertexColors: true,
      transparent: true,
      opacity: 0.38,
      sizeAttenuation: true,
      depthWrite: false,
    });

    this.points = new THREE.Points(geo, mat);
    this.scene.add(this.points);

    window.addEventListener('mousemove', this.onMouse);
    window.addEventListener('resize',    this.onResize);

    this.loop();
  }

  private loop = () => {
    this.animId = requestAnimationFrame(this.loop);

    if (!this.prefersReduced) {
      this.t += 0.0018;
      if (this.points) {
        this.points.rotation.y = this.t * 0.055 + this.mouseX * 0.00012;
        this.points.rotation.x = this.t * 0.022 + this.mouseY * 0.00007;
      }
    }

    this.renderer?.render(this.scene, this.camera);
  };

  private onMouse = (e: MouseEvent) => {
    this.mouseX = e.clientX - window.innerWidth  / 2;
    this.mouseY = e.clientY - window.innerHeight / 2;
  };

  private onResize = () => {
    const W = window.innerWidth;
    const H = window.innerHeight;
    this.camera.aspect = W / H;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(W, H);
  };
}
