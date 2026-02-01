import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'woven-onboarding-shell',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="bg">
      <!-- subtle brand (global) -->
      <div class="top">
        <div class="brand">WOVEN</div>
        <div class="meta" *ngIf="stepLabel">{{ stepLabel }}</div>
      </div>

      <!-- watermark W -->
      <div class="watermark">W</div>

      <div class="wrap">
        <div class="card">
          <div class="cardBrand">WOVEN</div>

          <div class="progressRow" *ngIf="totalSteps && stepNumber">
            <div class="progressText">{{ stepNumber }} / {{ totalSteps }}</div>
            <div class="bar" aria-hidden="true">
              <div class="fill" [style.width.%]="progressPct"></div>
            </div>
          </div>

          <h1 class="title">{{ title }}</h1>
          <p class="sub" *ngIf="subtitle">{{ subtitle }}</p>

          <ng-content></ng-content>

          <div class="footer">
            <div class="hint">You can edit this later.</div>
            <div class="tiny">W</div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    /* ===== Base ===== */
    .bg {
      min-height: 100vh;
      color: #0f0f0f;
      font-family: -apple-system, BlinkMacSystemFont, 'SF Pro Display', 'Segoe UI', Roboto, sans-serif;
      position: relative;
      overflow: visible;
      background: transparent;
    }

    /* ===== Top Bar ===== */
    .top {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      padding: 18px 24px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      pointer-events: none;
      z-index: 5;
      background: linear-gradient(to bottom, rgba(255,255,255,0.9) 0%, rgba(255,255,255,0) 100%);
    }

    .brand {
      letter-spacing: 0.25em;
      font-weight: 800;
      font-size: 11px;
      text-transform: uppercase;
      color: #0f0f0f;
      opacity: 0.85;
    }

    .meta {
      font-size: 11px;
      font-weight: 600;
      color: #0f0f0f;
      opacity: 0.5;
      letter-spacing: 0.02em;
    }

    /* ===== Watermark ===== */
    .watermark {
      position: absolute;
      right: -50px;
      top: -40px;
      font-size: 280px;
      font-weight: 900;
      letter-spacing: -0.08em;
      color: rgba(0, 0, 0, 0.025);
      transform: rotate(-12deg);
      user-select: none;
      pointer-events: none;
      z-index: 0;
    }

    /* ===== Content Wrapper ===== */
    .wrap {
      max-width: 560px;
      margin: 0 auto;
      padding: 100px 20px 60px;
      position: relative;
      z-index: 2;
    }

    /* ===== Card ===== */
    .card {
      position: relative;
      background: rgba(255, 255, 255, 0.96);
      border: 1px solid rgba(0, 0, 0, 0.06);
      border-radius: 24px;
      padding: 28px;
      box-shadow:
        0 1px 2px rgba(0, 0, 0, 0.04),
        0 4px 12px rgba(0, 0, 0, 0.04),
        0 16px 48px rgba(0, 0, 0, 0.06);
      backdrop-filter: blur(20px);
      -webkit-backdrop-filter: blur(20px);
    }

    .cardBrand {
      position: absolute;
      top: 16px;
      right: 20px;
      font-size: 8px;
      letter-spacing: 0.3em;
      font-weight: 800;
      opacity: 0.15;
      pointer-events: none;
      user-select: none;
      text-transform: uppercase;
    }

    /* ===== Progress ===== */
    .progressRow {
      display: flex;
      flex-direction: column;
      gap: 10px;
      margin-bottom: 20px;
    }

    .progressText {
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.04em;
      color: #0f0f0f;
      opacity: 0.45;
    }

    .bar {
      height: 6px;
      background: rgba(0, 0, 0, 0.06);
      border-radius: 100px;
      overflow: hidden;
    }

    .fill {
      height: 100%;
      background: linear-gradient(90deg, #0f0f0f 0%, #2a2a2a 100%);
      border-radius: 100px;
      transition: width 0.4s cubic-bezier(0.4, 0, 0.2, 1);
    }

    /* ===== Typography ===== */
    .title {
      margin: 0 0 8px;
      font-size: 28px;
      font-weight: 700;
      letter-spacing: -0.025em;
      line-height: 1.15;
      color: #0f0f0f;
    }

    .sub {
      margin: 0 0 24px;
      font-size: 14px;
      font-weight: 450;
      line-height: 1.55;
      color: #0f0f0f;
      opacity: 0.6;
      max-width: 48ch;
    }

    /* ===== Footer ===== */
    .footer {
      margin-top: 24px;
      padding-top: 18px;
      border-top: 1px solid rgba(0, 0, 0, 0.06);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .hint {
      font-size: 11px;
      font-weight: 500;
      color: #0f0f0f;
      opacity: 0.4;
      letter-spacing: 0.01em;
    }

    .tiny {
      width: 24px;
      height: 24px;
      border-radius: 100px;
      display: flex;
      justify-content: center;
      align-items: center;
      border: 1px solid rgba(0, 0, 0, 0.1);
      font-size: 10px;
      font-weight: 800;
      letter-spacing: 0.05em;
      color: #0f0f0f;
      opacity: 0.5;
      transition: all 0.2s ease;
    }

    .tiny:hover {
      opacity: 0.8;
      transform: scale(1.05);
    }

    /* ===== Responsive ===== */
    @media (max-width: 520px) {
      .watermark {
        font-size: 200px;
        right: -60px;
        top: -50px;
      }

      .card {
        padding: 22px;
        border-radius: 20px;
      }

      .title {
        font-size: 24px;
      }

      .wrap {
        padding: 90px 16px 48px;
      }

      .top {
        padding: 14px 18px;
      }

      .cardBrand {
        font-size: 7px;
        top: 14px;
        right: 16px;
      }
    }

    @media (max-width: 380px) {
      .card {
        padding: 18px;
        border-radius: 18px;
      }

      .title {
        font-size: 22px;
      }

      .sub {
        font-size: 13px;
      }
    }
  `]
})
export class OnboardingShellComponent {
  @Input() title = '';
  @Input() subtitle = '';
  @Input() stepNumber = 0;
  @Input() totalSteps = 6;
  @Input() stepLabel = '';

  get progressPct() {
    if (!this.totalSteps || !this.stepNumber) return 0;
    return Math.min(100, Math.max(0, (this.stepNumber / this.totalSteps) * 100));
  }
}