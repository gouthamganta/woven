import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-unmatch-rating',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="unmatch-overlay" (click)="cancel()">
      <div class="unmatch-card" (click)="$event.stopPropagation()">
        <h2>Before you go...</h2>
        <p>How was your experience with {{ otherName }}?</p>
        
        <div class="rating-section">
          <div class="slider-container">
            <div class="slider-track">
              <div class="slider-fill" 
                   [style.width.%]="sliderFillPercent" 
                   [class.negative]="rating < 0"
                   [class.positive]="rating > 0"></div>
            </div>
            <input 
              type="range" 
              min="-100" 
              max="100" 
              [(ngModel)]="rating" 
              class="rating-slider"
            />
          </div>
          <div class="rating-labels">
            <span class="red-label">🔴 Red Flags</span>
            <span class="rating-value">{{ rating > 0 ? '+' : '' }}{{ rating }}</span>
            <span class="green-label">Green Flags 🟢</span>
          </div>
        </div>

        <div class="buttons">
          <button (click)="skip()" class="btn-skip">Skip</button>
          <button (click)="submit()" class="btn-submit">Submit & Unmatch</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .unmatch-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.85);
      backdrop-filter: blur(8px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 9999;
      padding: 20px;
    }

    .unmatch-card {
      background: #1a1a1a;
      border-radius: 20px;
      padding: 32px 28px;
      max-width: 400px;
      width: 100%;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
    }

    h2 {
      margin: 0 0 8px;
      font-size: 24px;
      color: #fff;
    }

    p {
      margin: 0 0 24px;
      color: rgba(255, 255, 255, 0.7);
      font-size: 15px;
    }

    .rating-section {
      margin-bottom: 24px;
      padding: 20px;
      background: rgba(255, 255, 255, 0.05);
      border-radius: 12px;
    }

    .slider-container {
      position: relative;
      margin-bottom: 16px;
      padding: 12px 0;
    }

    .slider-track {
      position: absolute;
      top: 50%;
      left: 0;
      right: 0;
      height: 8px;
      background: rgba(255, 255, 255, 0.1);
      border-radius: 4px;
      transform: translateY(-50%);
      pointer-events: none;
      overflow: hidden;
    }

    .slider-fill {
      position: absolute;
      height: 100%;
      border-radius: 4px;
      transition: width 0.15s ease;
    }

    .slider-fill.negative {
      background: linear-gradient(90deg, #ff4444 0%, #cc0000 100%);
      right: 50%;
      left: auto;
    }

    .slider-fill.positive {
      background: linear-gradient(90deg, #00cc00 0%, #44ff44 100%);
      left: 50%;
    }

    .rating-slider {
      width: 100%;
      height: 44px;
      -webkit-appearance: none;
      background: transparent;
      position: relative;
      z-index: 1;
      cursor: pointer;
    }

    .rating-slider::-webkit-slider-thumb {
      -webkit-appearance: none;
      width: 28px;
      height: 28px;
      border-radius: 50%;
      background: #fff;
      cursor: pointer;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
    }

    .rating-slider::-moz-range-thumb {
      width: 28px;
      height: 28px;
      border-radius: 50%;
      background: #fff;
      cursor: pointer;
      border: none;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
    }

    .rating-labels {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 13px;
    }

    .red-label {
      color: #ff4444;
      font-weight: 500;
    }

    .green-label {
      color: #44ff44;
      font-weight: 500;
    }

    .rating-value {
      font-size: 20px;
      font-weight: 700;
      color: #fff;
    }

    .buttons {
      display: flex;
      gap: 12px;
    }

    button {
      flex: 1;
      padding: 16px;
      border: none;
      border-radius: 12px;
      font-size: 16px;
      font-weight: 700;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-skip {
      background: rgba(255, 255, 255, 0.1);
      color: rgba(255, 255, 255, 0.7);
    }

    .btn-skip:hover {
      background: rgba(255, 255, 255, 0.15);
    }

    .btn-submit {
      background: rgba(255, 68, 68, 0.2);
      color: #ff4444;
    }

    .btn-submit:hover {
      background: rgba(255, 68, 68, 0.3);
    }
  `]
})
export class UnmatchRatingComponent {
  @Input() otherName!: string;
  @Output() confirmed = new EventEmitter<number | undefined>();
  @Output() cancelled = new EventEmitter<void>();

  rating = 0;

  get sliderFillPercent(): number {
    if (this.rating === 0) return 0;
    return Math.abs(this.rating) / 2;
  }

  skip() {
    this.confirmed.emit(undefined);
  }

  submit() {
    this.confirmed.emit(this.rating);
  }

  cancel() {
    this.cancelled.emit();
  }
}