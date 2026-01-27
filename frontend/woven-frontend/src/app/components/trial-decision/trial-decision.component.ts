import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-trial-decision',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="modal-backdrop" (click)="close()">
      <div class="modal-content" (click)="$event.stopPropagation()">
        <div class="modal-header">
          <h2>Trial Period Ended</h2>
          <p class="subtitle">How was your trial with {{ otherName }}?</p>
        </div>

        <div class="modal-body">
          <!-- Rating slider (only for User A) -->
          <div class="rating-section" *ngIf="isUserA">
            <label>Rate your experience:</label>
            <div class="rating-slider-container">
              <span class="rating-label negative">-100</span>
              <input
                type="range"
                min="-100"
                max="100"
                step="1"
                [(ngModel)]="rating"
                class="rating-slider"
                [class.negative]="rating < 0"
                [class.positive]="rating > 0"
              />
              <span class="rating-label positive">+100</span>
            </div>
            <div class="rating-value" [class.negative]="rating < 0" [class.positive]="rating > 0">
              {{ rating > 0 ? '+' : '' }}{{ rating }}
            </div>
          </div>

          <div class="waiting-notice" *ngIf="!isUserA">
            <p>Waiting for your match to rate the trial...</p>
          </div>

          <!-- Decision buttons -->
          <div class="decision-buttons">
            <button
              class="btn btn-continue"
              [disabled]="submitting"
              (click)="submitDecision('CONTINUE')"
            >
              Continue Match
            </button>
            <button
              class="btn btn-end"
              [disabled]="submitting"
              (click)="submitDecision('END')"
            >
              End Match
            </button>
          </div>

          <p class="hint" *ngIf="isUserA">
            Your rating helps improve match quality for everyone.
          </p>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .modal-backdrop {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.6);
      backdrop-filter: blur(4px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
    }

    .modal-content {
      background: white;
      border-radius: 20px;
      width: 100%;
      max-width: 380px;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
      overflow: hidden;
    }

    .modal-header {
      padding: 24px 24px 16px;
      text-align: center;
      border-bottom: 1px solid rgba(0, 0, 0, 0.08);

      h2 {
        margin: 0;
        font-size: 20px;
        font-weight: 800;
        letter-spacing: -0.02em;
      }

      .subtitle {
        margin: 8px 0 0;
        font-size: 14px;
        opacity: 0.7;
      }
    }

    .modal-body {
      padding: 24px;
    }

    .rating-section {
      margin-bottom: 24px;

      label {
        display: block;
        font-size: 13px;
        font-weight: 700;
        margin-bottom: 12px;
        opacity: 0.8;
      }
    }

    .rating-slider-container {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .rating-label {
      font-size: 11px;
      font-weight: 800;
      min-width: 32px;
      text-align: center;

      &.negative { color: #ef4444; }
      &.positive { color: #22c55e; }
    }

    .rating-slider {
      flex: 1;
      -webkit-appearance: none;
      appearance: none;
      height: 8px;
      border-radius: 4px;
      background: linear-gradient(to right, #ef4444 0%, #fbbf24 50%, #22c55e 100%);
      outline: none;

      &::-webkit-slider-thumb {
        -webkit-appearance: none;
        appearance: none;
        width: 24px;
        height: 24px;
        border-radius: 50%;
        background: white;
        border: 3px solid #333;
        cursor: pointer;
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2);
      }

      &::-moz-range-thumb {
        width: 24px;
        height: 24px;
        border-radius: 50%;
        background: white;
        border: 3px solid #333;
        cursor: pointer;
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2);
      }
    }

    .rating-value {
      text-align: center;
      margin-top: 12px;
      font-size: 28px;
      font-weight: 900;
      letter-spacing: -0.02em;

      &.negative { color: #ef4444; }
      &.positive { color: #22c55e; }
    }

    .waiting-notice {
      text-align: center;
      padding: 20px;
      background: rgba(0, 0, 0, 0.03);
      border-radius: 12px;
      margin-bottom: 20px;

      p {
        margin: 0;
        font-size: 14px;
        opacity: 0.7;
      }
    }

    .decision-buttons {
      display: grid;
      gap: 12px;
    }

    .btn {
      width: 100%;
      padding: 14px 20px;
      border: none;
      border-radius: 12px;
      font-size: 15px;
      font-weight: 800;
      cursor: pointer;
      transition: all 0.2s ease;

      &:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
    }

    .btn-continue {
      background: linear-gradient(135deg, #22c55e, #16a34a);
      color: white;

      &:hover:not(:disabled) {
        transform: translateY(-1px);
        box-shadow: 0 4px 12px rgba(34, 197, 94, 0.4);
      }
    }

    .btn-end {
      background: rgba(0, 0, 0, 0.06);
      color: #333;

      &:hover:not(:disabled) {
        background: rgba(0, 0, 0, 0.1);
      }
    }

    .hint {
      margin: 16px 0 0;
      text-align: center;
      font-size: 12px;
      opacity: 0.5;
    }
  `]
})
export class TrialDecisionComponent {
  @Input() otherName: string = 'your match';
  @Input() isUserA: boolean = false;
  @Input() threadId: string = '';

  @Output() decided = new EventEmitter<{ decision: 'CONTINUE' | 'END'; rating?: number; result?: any }>();
  @Output() closed = new EventEmitter<void>();

  rating: number = 0;
  submitting = false;

  close() {
    this.closed.emit();
  }

  submitDecision(decision: 'CONTINUE' | 'END') {
    if (this.submitting) return;
    this.submitting = true;

    this.decided.emit({
      decision,
      rating: this.isUserA ? this.rating : undefined
    });
  }
}
