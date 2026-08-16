import {
  Component, Input, Output, EventEmitter,
  ChangeDetectionStrategy, ChangeDetectorRef
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { CoachingService, CoachingSummary } from '../../services/coaching.service';

@Component({
  selector: 'woven-coaching-card',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="overlay" *ngIf="summary">
      <div class="card">

        <div class="summaryText">{{ summary.summaryText }}</div>

        <div class="actions">
          <button class="gotIt" (click)="dismiss()" [disabled]="busy">
            {{ busy ? '…' : 'Got it' }}
          </button>
          <button class="turnOff" (click)="optOut()" [disabled]="busy">
            Turn this off
          </button>
        </div>

        <div class="confirming" *ngIf="confirming">
          Weekly reflections turned off. You can re-enable in Settings.
        </div>

      </div>
    </div>
  `,
  styles: [`
    .overlay {
      position: fixed; inset: 0; z-index: 300;
      background: var(--bg-base);
      display: flex; align-items: center; justify-content: center;
      padding: 24px 20px;
    }

    .card {
      max-width: 480px; width: 100%;
      display: flex; flex-direction: column; gap: 28px;
    }

    .summaryText {
      font-family: var(--font-display);
      font-size: 18px;
      line-height: 1.65;
      font-weight: 300;
      color: var(--text-primary);
      letter-spacing: -0.01em;
    }

    .actions {
      display: flex; flex-direction: column; gap: 10px;
    }

    .gotIt {
      width: 100%; padding: 16px 24px;
      border: none; border-radius: var(--radius-xl);
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base);
      font-family: var(--font-ui); font-size: 15px; font-weight: 700;
      cursor: pointer;
      transition: opacity 0.2s ease, transform 0.15s ease;
      box-shadow: 0 4px 20px rgba(212,160,23,0.28);
    }
    .gotIt:hover:not(:disabled) { opacity: 0.88; }
    .gotIt:active:not(:disabled) { transform: scale(0.96); }
    .gotIt:disabled { opacity: 0.4; cursor: not-allowed; }

    .turnOff {
      width: 100%; padding: 14px 24px;
      border: 1px solid var(--border-subtle); border-radius: var(--radius-xl);
      background: transparent;
      color: var(--text-dim);
      font-family: var(--font-ui); font-size: 13px;
      cursor: pointer;
      transition: all 0.2s ease;
    }
    .turnOff:hover:not(:disabled) { color: var(--text-muted); border-color: var(--border-soft); }
    .turnOff:disabled { opacity: 0.4; cursor: not-allowed; }

    .confirming {
      font-size: 12px; color: var(--text-dim); text-align: center;
      animation: fadeIn 0.3s ease;
    }
    @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
  `],
})
export class CoachingCardComponent {
  @Input() summary: CoachingSummary | null = null;
  @Output() dismissed = new EventEmitter<void>();

  busy = false;
  confirming = false;

  constructor(
    private coaching: CoachingService,
    private cdr: ChangeDetectorRef
  ) {}

  async dismiss() {
    if (!this.summary || this.busy) return;
    this.busy = true;
    this.cdr.markForCheck();
    try {
      await firstValueFrom(this.coaching.dismiss(this.summary.id));
    } catch { /* non-critical */ }
    this.dismissed.emit();
  }

  async optOut() {
    if (this.busy) return;
    this.busy = true;
    this.confirming = false;
    this.cdr.markForCheck();
    try {
      await firstValueFrom(this.coaching.optOut());
      this.confirming = true;
      this.cdr.markForCheck();
      await new Promise(r => setTimeout(r, 1800));
    } catch { /* non-critical */ }
    this.dismissed.emit();
  }
}
