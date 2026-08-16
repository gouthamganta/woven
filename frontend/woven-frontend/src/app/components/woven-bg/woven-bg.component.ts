import {
  Component, AfterViewInit, OnDestroy, ElementRef, ViewChild,
  Inject, PLATFORM_ID, ChangeDetectionStrategy,
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

interface Particle {
  x: number;
  y: number;
  vy: number;
  vx: number;
  size: number;
  opacity: number;
  char: string;
  r: number;       // current rotation
  rs: number;      // rotation speed
  color: string;
}

const SYMBOLS = ['♡', '∞', '✦', '◇', '✧', '≈', '♡', '∞', '✦'];  // weighted toward ♡ ∞ ✦
const COLORS  = [
  'rgba(192, 57,  43,  OP)',  // crimson
  'rgba(212, 160, 23,  OP)',  // gold
  'rgba(127, 119, 221, OP)',  // violet
  'rgba(255, 248, 240, OP)',  // warm white
];

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
export class WovenBgComponent implements AfterViewInit, OnDestroy {
  @ViewChild('c', { static: true }) canvasRef!: ElementRef<HTMLCanvasElement>;

  private rafId   = 0;
  private parts:  Particle[] = [];
  private W = 0;
  private H = 0;
  private mouseX = 0;
  private mouseY = 0;

  constructor(@Inject(PLATFORM_ID) private pid: object) {}

  ngAfterViewInit() {
    if (!isPlatformBrowser(this.pid)) return;
    this.setup();
    window.addEventListener('resize',    this.onResize);
    window.addEventListener('mousemove', this.onMouse);
    this.loop();
  }

  ngOnDestroy() {
    cancelAnimationFrame(this.rafId);
    window.removeEventListener('resize',    this.onResize);
    window.removeEventListener('mousemove', this.onMouse);
  }

  private setup() {
    const canvas = this.canvasRef.nativeElement;
    this.W = canvas.width  = window.innerWidth;
    this.H = canvas.height = window.innerHeight;
    this.parts = [];

    const count = Math.min(55, Math.floor((this.W * this.H) / 22000));
    for (let i = 0; i < count; i++) {
      this.parts.push(this.makeParticle(true));
    }
  }

  private makeParticle(anywhere = false): Particle {
    const char  = SYMBOLS[Math.floor(Math.random() * SYMBOLS.length)];
    const op    = 0.055 + Math.random() * 0.10;
    const color = COLORS[Math.floor(Math.random() * COLORS.length)]
                        .replace('OP', op.toFixed(3));
    return {
      x:  Math.random() * this.W,
      y:  anywhere ? Math.random() * this.H : this.H + 20,
      vy: 0.18 + Math.random() * 0.28,        // slow upward drift
      vx: (Math.random() - 0.5) * 0.12,       // gentle horizontal sway
      size: 11 + Math.random() * 14,
      opacity: op,
      char,
      color,
      r:  Math.random() * Math.PI * 2,
      rs: (Math.random() - 0.5) * 0.004,      // very slow rotation
    };
  }

  private loop = () => {
    this.rafId = requestAnimationFrame(this.loop);

    const canvas = this.canvasRef.nativeElement;
    const ctx    = canvas.getContext('2d')!;

    ctx.clearRect(0, 0, this.W, this.H);

    // Normalised mouse offset (-0.5 → 0.5)
    const mx = (this.mouseX / this.W) - 0.5;
    const my = (this.mouseY / this.H) - 0.5;

    for (const p of this.parts) {
      // Depth factor: bigger symbols are "closer", react more to mouse
      const depth = (p.size - 11) / 14;           // 0 (far) → 1 (close)
      const dx = mx * 70 * depth;
      const dy = my * 45 * depth;

      ctx.save();
      ctx.translate(p.x + dx, p.y + dy);
      ctx.rotate(p.r);
      ctx.font         = `${p.size}px serif`;
      ctx.fillStyle    = p.color;
      ctx.textAlign    = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(p.char, 0, 0);
      ctx.restore();

      p.y  -= p.vy;
      p.x  += p.vx;
      p.r  += p.rs;

      // Respawn at bottom when floated off top
      if (p.y < -20) {
        const fresh = this.makeParticle(false);
        Object.assign(p, fresh);
      }
    }
  };

  private onMouse = (e: MouseEvent) => {
    this.mouseX = e.clientX;
    this.mouseY = e.clientY;
  };

  private onResize = () => {
    const canvas = this.canvasRef.nativeElement;
    this.W = canvas.width  = window.innerWidth;
    this.H = canvas.height = window.innerHeight;
  };
}
