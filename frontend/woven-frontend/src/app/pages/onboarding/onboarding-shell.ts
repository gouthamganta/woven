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
      background: linear-gradient(to bottom, rgba(26,15,30,0.92) 0%, rgba(26,15,30,0) 100%);
    }

    .brand {
      font-family: "DM Sans", system-ui, sans-serif;
      letter-spacing: 0.22em;
      font-weight: 700;
      font-size: 11px;
      text-transform: uppercase;
      color: rgba(255, 245, 250, 0.70);
    }

    .meta {
      font-family: "DM Sans", system-ui, sans-serif;
      font-size: 11px;
      font-weight: 500;
      color: rgba(255, 215, 235, 0.46);
      letter-spacing: 0.02em;
    }

    /* ===== Watermark ===== */
    .watermark {
      position: absolute;
      right: -50px;
      top: -40px;
      font-family: "Fraunces", Georgia, serif;
      font-size: 280px;
      font-weight: 900;
      letter-spacing: -0.08em;
      color: rgba(255, 255, 255, 0.04);
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
      background: rgba(34, 22, 40, 0.82);
      border: 1px solid rgba(255, 255, 255, 0.13);
      border-radius: 24px;
      padding: 28px;
      box-shadow:
        0 4px 20px rgba(0, 0, 0, 0.44),
        0 1px 0 rgba(255, 255, 255, 0.07) inset;
      backdrop-filter: blur(24px) saturate(1.5);
      -webkit-backdrop-filter: blur(24px) saturate(1.5);
    }

    /* Gradient shimmer on top edge */
    .card::before {
      content: "";
      position: absolute;
      top: 0;
      left: 20%;
      right: 20%;
      height: 1px;
      background: linear-gradient(90deg, transparent, rgba(224,84,144,0.6), rgba(125,91,208,0.6), transparent);
      border-radius: 9999px;
    }

    .cardBrand {
      position: absolute;
      top: 16px;
      right: 20px;
      font-family: "DM Sans", system-ui, sans-serif;
      font-size: 8px;
      letter-spacing: 0.3em;
      font-weight: 700;
      color: rgba(255, 245, 250, 0.18);
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
      font-family: "DM Sans", system-ui, sans-serif;
      font-size: 11px;
      font-weight: 600;
      letter-spacing: 0.04em;
      color: rgba(255, 215, 235, 0.46);
    }

    .bar {
      height: 5px;
      background: rgba(255, 255, 255, 0.08);
      border-radius: 100px;
      overflow: hidden;
    }

    .fill {
      height: 100%;
      background: linear-gradient(90deg, #E05490, #7D5BD0);
      border-radius: 100px;
      transition: width 0.4s cubic-bezier(0.4, 0, 0.2, 1);
      box-shadow: 0 0 12px rgba(224,84,144,0.4);
    }

    /* ===== Typography ===== */
    .title {
      font-family: "Fraunces", Georgia, serif;
      margin: 0 0 8px;
      font-size: 28px;
      font-weight: 400;
      letter-spacing: -0.025em;
      line-height: 1.15;
      color: rgba(255, 245, 250, 0.96);
    }

    .sub {
      font-family: "DM Sans", system-ui, sans-serif;
      margin: 0 0 24px;
      font-size: 14px;
      font-weight: 400;
      line-height: 1.55;
      color: rgba(255, 230, 242, 0.65);
      max-width: 48ch;
    }

    /* ===== Footer ===== */
    .footer {
      margin-top: 24px;
      padding-top: 18px;
      border-top: 1px solid rgba(255, 255, 255, 0.07);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .hint {
      font-family: "DM Sans", system-ui, sans-serif;
      font-size: 11px;
      font-weight: 500;
      color: rgba(255, 215, 235, 0.36);
      letter-spacing: 0.01em;
    }

    .tiny {
      width: 24px;
      height: 24px;
      border-radius: 100px;
      display: flex;
      justify-content: center;
      align-items: center;
      border: 1px solid rgba(255, 255, 255, 0.13);
      font-family: "DM Sans", system-ui, sans-serif;
      font-size: 10px;
      font-weight: 700;
      color: rgba(255, 245, 250, 0.40);
      transition: all 0.2s ease;
    }

    .tiny:hover {
      color: rgba(255, 245, 250, 0.80);
      border-color: rgba(255, 255, 255, 0.22);
      transform: scale(1.05);
    }

    /* ===== Responsive ===== */
    @media (max-width: 520px) {
      .watermark { font-size: 200px; right: -60px; top: -50px; }
      .card { padding: 22px; border-radius: 20px; }
      .title { font-size: 24px; }
      .wrap { padding: 90px 16px 48px; }
      .top { padding: 14px 18px; }
      .cardBrand { font-size: 7px; top: 14px; right: 16px; }
    }

    @media (max-width: 380px) {
      .card { padding: 18px; border-radius: 18px; }
      .title { font-size: 22px; }
      .sub { font-size: 13px; }
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