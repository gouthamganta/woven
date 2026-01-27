import { Component, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-how-it-works-sheet',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="overlay" (click)="close()"></div>
    <div class="sheet">
      <div class="handleWrap">
        <div class="handle"></div>
      </div>
      
      <!-- Hero Branding -->
      <div class="hero">
        <div class="logoMark">W</div>
        <h1 class="logo">WOVEN</h1>
        <div class="tagline">
          <span>Energy</span>
          <span class="sep">·</span>
          <span>Intention</span>
          <span class="sep">·</span>
          <span>Chemistry</span>
        </div>
        <p class="subtitle">Where every match has potential</p>
      </div>

      <!-- Scrollable Content -->
      <div class="scrollContent">
        
        <!-- Visual Flow -->
        <div class="flow">
          
          <!-- Step 1 -->
          <div class="step">
            <div class="stepCircle">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="3" width="7" height="7" rx="1"></rect>
                <rect x="14" y="3" width="7" height="7" rx="1"></rect>
                <rect x="14" y="14" width="7" height="7" rx="1"></rect>
                <rect x="3" y="14" width="7" height="7" rx="1"></rect>
              </svg>
            </div>
            <div class="stepContent">
              <h3>Daily Deck</h3>
              <p>5 profiles per day<br/>One at a time</p>
            </div>
          </div>

          <div class="arrow">→</div>

          <!-- Step 2 -->
          <div class="step">
            <div class="stepCircle">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"></circle>
                <path d="M12 6v6l4 2"></path>
              </svg>
            </div>
            <div class="stepContent">
              <h3>Choose Energy</h3>
              <p>Pick the moment<br/>Not the person</p>
            </div>
          </div>

          <div class="arrow">→</div>

          <!-- Step 3 -->
          <div class="step">
            <div class="stepCircle">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"></path>
              </svg>
            </div>
            <div class="stepContent">
              <h3>Match Opens</h3>
              <p>Pure or Edge<br/>Balloon chat starts</p>
            </div>
          </div>

        </div>

        <!-- Key Info Grid -->
        <div class="infoGrid">
          
          <div class="infoCard">
            <div class="infoIcon">⚡</div>
            <div class="infoText">
              <strong>5 Sparks Daily</strong>
              <span>Use them wisely</span>
            </div>
          </div>

          <div class="infoCard">
            <div class="infoIcon">💾</div>
            <div class="infoText">
              <strong>Save Up to 10</strong>
              <span>2 sparks/day max</span>
            </div>
          </div>

          <div class="infoCard">
            <div class="infoIcon">🎈</div>
            <div class="infoText">
              <strong>5-Min Balloon</strong>
              <span>Pop it or keep going</span>
            </div>
          </div>

          <div class="infoCard">
            <div class="infoIcon">🧠</div>
            <div class="infoText">
              <strong>8 Pillars Match</strong>
              <span>All private, better fits</span>
            </div>
          </div>

        </div>

        <!-- Match Types -->
        <div class="matchSection">
          <h4>Two Match Types</h4>
          <div class="matchGrid">
            <div class="matchCard">
              <div class="matchBadge pure">Pure</div>
              <p>Same choice<br/><strong>Full profiles visible</strong></p>
            </div>
            <div class="matchCard">
              <div class="matchBadge edge">Edge</div>
              <p>Different choice<br/><strong>Hidden until you chat</strong></p>
            </div>
          </div>
        </div>

        <!-- Philosophy -->
        <div class="philosophy">
          <div class="philItem">Limited choices</div>
          <div class="philItem">Emotional safety</div>
          <div class="philItem">Calm pacing</div>
          <div class="philItem">Real connections</div>
        </div>

        <p class="footer">AI writes explanations, never judges people</p>

      </div>

      <!-- Close -->
      <button class="closeBtn" (click)="close()">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
          <line x1="18" y1="6" x2="6" y2="18"></line>
          <line x1="6" y1="6" x2="18" y2="18"></line>
        </svg>
      </button>
    </div>
  `,
  styles: [`
    /* Overlay */
    .overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.45);
      backdrop-filter: blur(10px);
      z-index: 9998;
      animation: fadeIn 0.2s ease-out;
    }

    @keyframes fadeIn {
      from { opacity: 0; }
      to { opacity: 1; }
    }

    /* Sheet */
    .sheet {
      position: fixed;
      bottom: 0;
      left: 0;
      right: 0;
      max-height: 88vh;
      background: #fafafa;
      border-radius: 26px 26px 0 0;
      z-index: 9999;
      display: flex;
      flex-direction: column;
      animation: slideUp 0.3s cubic-bezier(0.33, 1, 0.68, 1);
      box-shadow: 0 -10px 40px rgba(0, 0, 0, 0.15);
      overflow: hidden;
    }

    @keyframes slideUp {
      from { transform: translateY(100%); }
      to { transform: translateY(0); }
    }

    /* Handle */
    .handleWrap {
      padding: 10px 0 6px;
      display: flex;
      justify-content: center;
      background: #fafafa;
      z-index: 10;
      flex-shrink: 0;
    }

    .handle {
      width: 36px;
      height: 4px;
      background: rgba(0, 0, 0, 0.2);
      border-radius: 2px;
    }

    /* Hero Branding */
    .hero {
      text-align: center;
      padding: 16px 24px 24px;
      background: linear-gradient(180deg, #fafafa 0%, #f5f5f5 100%);
      border-bottom: 1px solid rgba(0, 0, 0, 0.08);
      flex-shrink: 0;
    }

    .logoMark {
      font-size: 56px;
      font-weight: 900;
      letter-spacing: -0.05em;
      color: rgba(0, 0, 0, 0.06);
      line-height: 0.8;
      margin-bottom: 4px;
      user-select: none;
    }

    .logo {
      font-size: 28px;
      font-weight: 900;
      letter-spacing: 0.2em;
      text-transform: uppercase;
      margin: 0 0 10px;
      color: #111;
    }

    .tagline {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 7px;
      font-size: 12px;
      font-weight: 650;
      letter-spacing: 0.02em;
      color: rgba(0, 0, 0, 0.7);
      margin-bottom: 6px;
    }

    .tagline .sep {
      opacity: 0.3;
      font-weight: 400;
    }

    .subtitle {
      font-size: 13px;
      color: rgba(0, 0, 0, 0.55);
      margin: 0;
      font-weight: 500;
      font-style: italic;
    }

    /* Scrollable Content Container */
    .scrollContent {
      flex: 1;
      overflow-y: auto;
      -webkit-overflow-scrolling: touch;
      padding-bottom: 20px;
    }

    /* Visual Flow */
    .flow {
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 28px 20px;
      background: #ffffff;
      gap: 12px;
      overflow-x: auto;
      -webkit-overflow-scrolling: touch;
    }

    .step {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      flex: 1;
      min-width: 100px;
      animation: stepIn 0.5s cubic-bezier(0.34, 1.56, 0.64, 1) backwards;
    }

    .step:nth-child(1) { animation-delay: 0.1s; }
    .step:nth-child(3) { animation-delay: 0.2s; }
    .step:nth-child(5) { animation-delay: 0.3s; }

    @keyframes stepIn {
      from {
        opacity: 0;
        transform: scale(0.8) translateY(10px);
      }
      to {
        opacity: 1;
        transform: scale(1) translateY(0);
      }
    }

    .stepCircle {
      width: 64px;
      height: 64px;
      border-radius: 50%;
      background: linear-gradient(135deg, #f8f8f8 0%, #ffffff 100%);
      border: 2px solid rgba(0, 0, 0, 0.1);
      display: flex;
      align-items: center;
      justify-content: center;
      color: rgba(0, 0, 0, 0.75);
      transition: all 0.3s cubic-bezier(0.34, 1.56, 0.64, 1);
      flex-shrink: 0;
    }

    .step:hover .stepCircle {
      transform: scale(1.1) rotate(5deg);
      border-color: rgba(0, 0, 0, 0.2);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
    }

    .stepContent {
      text-align: center;
    }

    .stepContent h3 {
      font-size: 14px;
      font-weight: 750;
      margin: 0 0 4px;
      color: #111;
      letter-spacing: -0.01em;
    }

    .stepContent p {
      font-size: 11px;
      line-height: 1.4;
      color: rgba(0, 0, 0, 0.6);
      margin: 0;
      font-weight: 500;
    }

    .arrow {
      font-size: 20px;
      color: rgba(0, 0, 0, 0.25);
      font-weight: 300;
      flex-shrink: 0;
      animation: pulse 2s ease-in-out infinite;
    }

    @keyframes pulse {
      0%, 100% { opacity: 0.25; transform: translateX(0); }
      50% { opacity: 0.5; transform: translateX(3px); }
    }

    /* Info Grid */
    .infoGrid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 10px;
      padding: 20px;
      background: #fafafa;
    }

    .infoCard {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 14px;
      background: #ffffff;
      border: 1px solid rgba(0, 0, 0, 0.08);
      border-radius: 14px;
      transition: all 0.2s;
      animation: cardPop 0.4s cubic-bezier(0.34, 1.56, 0.64, 1) backwards;
    }

    .infoCard:nth-child(1) { animation-delay: 0.05s; }
    .infoCard:nth-child(2) { animation-delay: 0.1s; }
    .infoCard:nth-child(3) { animation-delay: 0.15s; }
    .infoCard:nth-child(4) { animation-delay: 0.2s; }

    @keyframes cardPop {
      from {
        opacity: 0;
        transform: scale(0.85);
      }
      to {
        opacity: 1;
        transform: scale(1);
      }
    }

    .infoCard:hover {
      transform: translateY(-3px) scale(1.02);
      box-shadow: 0 6px 16px rgba(0, 0, 0, 0.08);
      border-color: rgba(0, 0, 0, 0.12);
    }

    .infoIcon {
      font-size: 26px;
      flex-shrink: 0;
      line-height: 1;
    }

    .infoText {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .infoText strong {
      font-size: 13px;
      font-weight: 700;
      color: #111;
      line-height: 1.3;
    }

    .infoText span {
      font-size: 11px;
      color: rgba(0, 0, 0, 0.55);
      font-weight: 500;
    }

    /* Match Section */
    .matchSection {
      padding: 20px 20px 16px;
      background: #ffffff;
    }

    .matchSection h4 {
      font-size: 15px;
      font-weight: 750;
      margin: 0 0 12px;
      color: #111;
      text-align: center;
      letter-spacing: -0.01em;
    }

    .matchGrid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 10px;
    }

    .matchCard {
      padding: 14px;
      background: #fafafa;
      border: 1px solid rgba(0, 0, 0, 0.08);
      border-radius: 12px;
      text-align: center;
      transition: all 0.2s;
    }

    .matchCard:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.06);
    }

    .matchBadge {
      display: inline-block;
      padding: 6px 12px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 700;
      margin-bottom: 8px;
      letter-spacing: 0.02em;
    }

    .matchBadge.pure {
      background: rgba(0, 0, 0, 0.08);
      color: #111;
    }

    .matchBadge.edge {
      background: rgba(0, 0, 0, 0.06);
      color: rgba(0, 0, 0, 0.75);
    }

    .matchCard p {
      font-size: 11px;
      line-height: 1.5;
      color: rgba(0, 0, 0, 0.65);
      margin: 0;
      font-weight: 500;
    }

    .matchCard strong {
      color: #111;
      font-weight: 650;
    }

    /* Philosophy */
    .philosophy {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 8px;
      padding: 16px 20px;
      background: #fafafa;
    }

    .philItem {
      padding: 12px;
      background: #ffffff;
      border: 1px solid rgba(0, 0, 0, 0.08);
      border-radius: 10px;
      text-align: center;
      font-size: 11px;
      font-weight: 700;
      color: rgba(0, 0, 0, 0.75);
      letter-spacing: 0.02em;
      transition: all 0.2s;
    }

    .philItem:hover {
      background: #f5f5f5;
      color: #111;
      transform: scale(1.03);
    }

    /* Footer */
    .footer {
      font-size: 11px;
      color: rgba(0, 0, 0, 0.5);
      text-align: center;
      margin: 8px 20px 0;
      font-style: italic;
      font-weight: 500;
    }

    /* Close Button */
    .closeBtn {
      position: absolute;
      top: 16px;
      right: 16px;
      width: 32px;
      height: 32px;
      border-radius: 50%;
      border: 0;
      background: rgba(0, 0, 0, 0.1);
      backdrop-filter: blur(8px);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
      z-index: 11;
      color: rgba(0, 0, 0, 0.7);
    }

    .closeBtn:hover {
      background: rgba(0, 0, 0, 0.18);
      color: #111;
      transform: rotate(90deg) scale(1.1);
    }

    .closeBtn:active {
      transform: rotate(90deg) scale(0.9);
    }

    /* Scrollbar */
    .scrollContent::-webkit-scrollbar {
      width: 4px;
    }

    .scrollContent::-webkit-scrollbar-track {
      background: transparent;
    }

    .scrollContent::-webkit-scrollbar-thumb {
      background: rgba(0, 0, 0, 0.15);
      border-radius: 2px;
    }

    /* Mobile Optimization */
    @media (max-width: 480px) {
      .hero {
        padding: 14px 20px 20px;
      }

      .logoMark {
        font-size: 48px;
      }

      .logo {
        font-size: 24px;
      }

      .flow {
        padding: 20px 16px;
        gap: 8px;
      }

      .stepCircle {
        width: 56px;
        height: 56px;
      }

      .stepCircle svg {
        width: 28px;
        height: 28px;
      }

      .arrow {
        font-size: 16px;
      }

      .infoGrid {
        padding: 16px;
        gap: 8px;
      }

      .infoCard {
        padding: 12px;
      }

      .philosophy {
        grid-template-columns: 1fr 1fr;
        padding: 12px 16px;
      }

      .matchSection {
        padding: 16px;
      }
    }

    @media (max-width: 360px) {
      .infoGrid {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class HowItWorksSheetComponent {
  @Output() closed = new EventEmitter<void>();

  close() {
    this.closed.emit();
  }
}