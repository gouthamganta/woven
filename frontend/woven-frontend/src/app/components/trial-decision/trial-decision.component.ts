import { Component, Input, Output, EventEmitter, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';

export type TrialDecision = 'CONTINUE' | 'END' | 'BLOCK';
export type TrialEndReason = 'no_spark' | 'wrong_timing' | 'not_my_type';

@Component({
  selector: 'app-trial-decision',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="backdrop">
      <div class="card">

        <!-- Main decision view -->
        <ng-container *ngIf="!showReasonPicker">
          <div class="eyebrow">Trial ended</div>
          <div class="title">How was it with {{ otherName }}?</div>

          <div class="actions">
            <button class="btnContinue" [disabled]="submitting" (click)="onContinue()">
              Continue ◈
            </button>
            <button class="btnEnd" [disabled]="submitting" (click)="onEnd()">
              End match
            </button>
            <button class="btnBlock" [disabled]="submitting" (click)="onBlock()">
              Block
            </button>
          </div>
        </ng-container>

        <!-- Reason picker (shown after End) -->
        <ng-container *ngIf="showReasonPicker">
          <div class="eyebrow">One more thing</div>
          <div class="title">What felt off?</div>
          <div class="reasonHint">Optional · dismisses in {{ reasonCountdown }}s</div>

          <div class="reasons">
            <button class="reasonBtn" [class.active]="selectedReason === 'no_spark'"
                    (click)="selectReason('no_spark')">No spark</button>
            <button class="reasonBtn" [class.active]="selectedReason === 'wrong_timing'"
                    (click)="selectReason('wrong_timing')">Wrong timing</button>
            <button class="reasonBtn" [class.active]="selectedReason === 'not_my_type'"
                    (click)="selectReason('not_my_type')">Not my type</button>
          </div>

          <button class="btnSubmitReason" [disabled]="submitting" (click)="submitWithReason()">
            {{ submitting ? 'Saving…' : 'Done' }}
          </button>
        </ng-container>

      </div>
    </div>
  `,
  styles: [`
    .backdrop {
      position: fixed;
      inset: 0;
      z-index: 50;
      background: rgba(9, 5, 13, 0.75);
      backdrop-filter: blur(6px);
      -webkit-backdrop-filter: blur(6px);
      display: flex;
      align-items: flex-end;
      justify-content: center;
      padding-bottom: 24px;
    }

    .card {
      width: 100%;
      max-width: 480px;
      margin: 0 16px;
      background: rgba(28, 17, 36, 0.97);
      border: 1px solid rgba(212, 160, 23, 0.18);
      border-radius: 24px;
      padding: 28px 20px 24px;
      box-shadow: 0 24px 64px rgba(0,0,0,0.6);
    }

    .eyebrow {
      font-family: var(--font-ui);
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.2em;
      text-transform: uppercase;
      color: var(--text-muted);
      margin-bottom: 8px;
    }

    .title {
      font-family: var(--font-display);
      font-size: 22px;
      font-weight: 500;
      color: var(--text-primary);
      margin-bottom: 24px;
    }

    .actions {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .btnContinue {
      padding: 15px;
      border-radius: 14px;
      border: none;
      background: #8b1a4a;
      color: #fff;
      font-family: var(--font-ui);
      font-size: 15px;
      font-weight: 700;
      letter-spacing: 0.04em;
      cursor: pointer;
      transition: background 0.2s ease;

      &:hover:not(:disabled) { background: #a62058; }
      &:disabled { opacity: 0.45; cursor: default; }
      &:active:not(:disabled) { transform: scale(0.96); }
    }

    .btnEnd {
      padding: 14px;
      border-radius: 14px;
      border: 1px solid var(--border-soft);
      background: rgba(255,255,255,0.04);
      color: var(--text-secondary);
      font-family: var(--font-ui);
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.2s ease, border-color 0.2s ease;

      &:hover:not(:disabled) { background: rgba(255,255,255,0.08); }
      &:disabled { opacity: 0.45; cursor: default; }
    }

    .btnBlock {
      padding: 11px;
      border-radius: 14px;
      border: none;
      background: transparent;
      color: var(--text-muted);
      font-family: var(--font-ui);
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: color 0.2s ease;

      &:hover:not(:disabled) { color: #ff6b6b; }
      &:disabled { opacity: 0.45; cursor: default; }
    }

    /* Reason picker */
    .reasonHint {
      font-family: var(--font-ui);
      font-size: 12px;
      color: var(--text-muted);
      margin-bottom: 20px;
    }

    .reasons {
      display: flex;
      flex-direction: column;
      gap: 10px;
      margin-bottom: 20px;
    }

    .reasonBtn {
      padding: 14px;
      border-radius: 14px;
      border: 1px solid var(--border-soft);
      background: rgba(255,255,255,0.04);
      color: var(--text-secondary);
      font-family: var(--font-ui);
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      text-align: left;
      transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;

      &:hover { background: rgba(255,255,255,0.08); }
      &.active {
        border-color: rgba(139, 26, 74, 0.6);
        background: rgba(139, 26, 74, 0.15);
        color: var(--text-primary);
      }
    }

    .btnSubmitReason {
      width: 100%;
      padding: 14px;
      border-radius: 14px;
      border: none;
      background: rgba(255,255,255,0.08);
      color: var(--text-primary);
      font-family: var(--font-ui);
      font-size: 14px;
      font-weight: 700;
      cursor: pointer;
      transition: background 0.2s ease;

      &:hover:not(:disabled) { background: rgba(255,255,255,0.13); }
      &:disabled { opacity: 0.45; cursor: default; }
    }
  `]
})
export class TrialDecisionComponent implements OnDestroy {
  @Input() otherName = 'your match';

  @Output() decided = new EventEmitter<{ decision: TrialDecision; endReason?: TrialEndReason }>();

  submitting = false;
  showReasonPicker = false;
  selectedReason: TrialEndReason | null = null;
  reasonCountdown = 30;

  private reasonTimer: any;
  private countdownInterval: any;

  onContinue() {
    if (this.submitting) return;
    this.submitting = true;
    this.decided.emit({ decision: 'CONTINUE' });
  }

  onEnd() {
    if (this.submitting) return;
    this.showReasonPicker = true;
    this.startReasonCountdown();
  }

  onBlock() {
    if (this.submitting) return;
    this.submitting = true;
    this.decided.emit({ decision: 'BLOCK' });
  }

  selectReason(reason: TrialEndReason) {
    this.selectedReason = reason;
  }

  submitWithReason() {
    if (this.submitting) return;
    this.submitting = true;
    this.clearTimers();
    this.decided.emit({
      decision: 'END',
      endReason: this.selectedReason ?? undefined
    });
  }

  private startReasonCountdown() {
    this.reasonCountdown = 30;
    this.countdownInterval = setInterval(() => {
      this.reasonCountdown--;
      if (this.reasonCountdown <= 0) {
        this.clearTimers();
        this.submitWithReason();
      }
    }, 1000);
  }

  private clearTimers() {
    if (this.reasonTimer) clearTimeout(this.reasonTimer);
    if (this.countdownInterval) clearInterval(this.countdownInterval);
  }

  ngOnDestroy() {
    this.clearTimers();
  }
}
