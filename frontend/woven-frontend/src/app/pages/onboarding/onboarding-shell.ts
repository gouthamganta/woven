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
    .bg{
      min-height:100vh;
      color:#111;
      font-family: inherit;
      position: relative;
      /* CRITICAL FIX: remove overflow:hidden so WOW layer shows */
      overflow: visible;
      /* Keep background transparent to let WOW layer show through */
      background: transparent;
    }

    .top{
      position: fixed;
      top: 20px;
      left: 22px;
      right: 22px;
      display:flex;
      justify-content:space-between;
      align-items:center;
      pointer-events:none;
      opacity:.92;
      z-index: 5;
    }

    .brand{
      letter-spacing:.22em;
      font-weight:750;
      font-size:12px;
      text-transform: uppercase;
    }

    .meta{
      font-size:12px;
      opacity:.65;
    }

    .watermark{
      position:absolute;
      right:-40px;
      top: -30px;
      font-size: 240px;
      font-weight: 800;
      letter-spacing: -0.06em;
      color: rgba(0,0,0,0.035);
      transform: rotate(-8deg);
      user-select:none;
      pointer-events:none;
      z-index: 0;
    }

    .wrap{
      max-width: 620px;
      margin: 0 auto;
      padding: 92px 18px 44px;
      position: relative;
      z-index: 2;
    }

    .card{
      position: relative;
      background: rgba(255,255,255,0.94);
      border: 1px solid rgba(0,0,0,.08);
      border-radius: 20px;
      padding: 22px;
      box-shadow: 0 16px 40px rgba(0,0,0,.08);
      backdrop-filter: blur(12px);
    }

    .cardBrand{
      position: absolute;
      top: 14px;
      right: 16px;
      font-size: 9px;
      letter-spacing: .28em;
      font-weight: 800;
      opacity: .22;
      pointer-events: none;
      user-select: none;
    }

    .progressRow{
      display:grid;
      gap:10px;
      margin-bottom: 16px;
    }

    .progressText{
      font-size:12px;
      opacity:.65;
    }

    .bar{
      height: 7px;
      background: rgba(0,0,0,.08);
      border-radius: 999px;
      overflow:hidden;
    }

    .fill{
      height:100%;
      background:#111;
      border-radius:999px;
      transition: width .25s ease;
    }

    .title{
      margin:0 0 6px;
      font-size: 26px;
      letter-spacing:-0.02em;
      line-height: 1.15;
    }

    .sub{
      margin:0 0 18px;
      font-size: 14px;
      opacity:.74;
      line-height:1.45;
      max-width: 54ch;
    }

    .footer{
      margin-top: 18px;
      padding-top: 14px;
      border-top: 1px solid rgba(0,0,0,.07);
      display:flex;
      justify-content:space-between;
      font-size:12px;
      opacity:.62;
      align-items:center;
    }

    .tiny{
      width: 22px;
      height: 22px;
      border-radius: 999px;
      display:flex;
      justify-content:center;
      align-items:center;
      border: 1px solid rgba(0,0,0,.12);
      font-weight: 750;
      letter-spacing: .08em;
    }

    @media (max-width: 520px){
      .watermark{ font-size: 190px; right:-55px; top:-40px; }
      .card{ padding: 18px; border-radius: 18px; }
      .title{ font-size: 22px; }
      .wrap{ padding-top: 84px; }
      .cardBrand{ font-size: 8px; }
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