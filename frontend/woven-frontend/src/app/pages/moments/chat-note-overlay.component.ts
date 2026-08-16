import {
  Component,
  Input,
  Output,
  EventEmitter,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export type OverlayCard = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
  bridgeQuestion?: string | null;
};

const STARTERS = [
  'Your photo made me want to say…',
  'I\'d love to talk about…',
  'Something tells me we\'d…',
];

@Component({
  selector: 'woven-chat-note-overlay',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="overlay" *ngIf="card">

      <!-- Photo area -->
      <div class="photoWrap">
        <button class="backBtn" (click)="onBack()" aria-label="Back">
          <svg width="18" height="18" viewBox="0 0 18 18" fill="none">
            <path d="M11 4L6 9L11 14" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        </button>

        <img class="photo" *ngIf="card.profilePhoto; else fallback"
          [src]="card.profilePhoto" [alt]="card.fullName" />
        <ng-template #fallback>
          <div class="photoFallback">{{ card.fullName.slice(0, 1) }}</div>
        </ng-template>

        <div class="photoGrad">
          <span class="name">{{ card.fullName }}</span>
          <span class="choiceBadge" [class.magical]="choice === 'MAGICAL'" [class.logical]="choice === 'LOGICAL'">
            <ng-container *ngIf="choice === 'MAGICAL'">◈ Magical</ng-container>
            <ng-container *ngIf="choice === 'LOGICAL'">◇ Resonant</ng-container>
          </span>
        </div>
      </div>

      <!-- Writing area -->
      <div class="writeWrap">

        <!-- Bridge question suggestion -->
        <div class="bridgeWrap" *ngIf="card!.bridgeQuestion">
          <span class="bridgeLabel">Try asking</span>
          <button class="bridgeQuestion" (click)="applyBridgeQuestion()">
            {{ card!.bridgeQuestion }}
          </button>
        </div>

        <!-- Starter dropdown -->
        <div class="starterWrap">
          <button class="starterTrigger" (click)="toggleDropdown()">
            <span class="starterLabel">Need a starter?</span>
            <span class="dropArrow" [class.open]="showDropdown">▾</span>
          </button>
          <div class="starterList" *ngIf="showDropdown">
            <button
              *ngFor="let s of starters"
              class="starterItem"
              (click)="applyStarter(s)"
            >{{ s }}</button>
          </div>
        </div>

        <textarea
          class="noteInput"
          [(ngModel)]="noteText"
          placeholder="Write your opening note…"
          maxlength="150"
          rows="4"
          (input)="onInput()"
        ></textarea>

        <div class="metaRow">
          <span class="minHint" *ngIf="noteText.length > 0 && noteText.length < 20">
            {{ 20 - noteText.length }} more to go
          </span>
          <span class="charCount" [class.warn]="noteText.length > 130">
            {{ noteText.length }} / 150
          </span>
        </div>

        <button
          class="sendBtn"
          [class.magical]="choice === 'MAGICAL'"
          [class.logical]="choice === 'LOGICAL'"
          [disabled]="!canSend || submitting"
          (click)="onSend()"
        >
          <span *ngIf="!submitting">
            <ng-container *ngIf="choice === 'MAGICAL'">Send ◈</ng-container>
            <ng-container *ngIf="choice === 'LOGICAL'">Send ◇ Resonant</ng-container>
          </span>
          <span *ngIf="submitting">Sending…</span>
        </button>

      </div>
    </div>
  `,
  styles: [`
    .overlay {
      position: fixed; inset: 0; z-index: 200;
      display: flex; flex-direction: column;
      background: var(--bg-base);
    }

    /* ── Photo ── */
    .photoWrap {
      position: relative;
      flex: 0 0 50dvh;
      overflow: hidden;
      background: var(--bg-sunken);
    }

    .backBtn {
      position: absolute; top: 14px; left: 14px; z-index: 10;
      width: 38px; height: 38px;
      background: rgba(0, 0, 0, 0.45);
      border: none; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
      cursor: pointer;
      backdrop-filter: blur(8px);
      transition: background 0.15s ease;
    }
    .backBtn:hover { background: rgba(0, 0, 0, 0.65); }

    .photo {
      width: 100%; height: 100%;
      object-fit: cover; display: block;
    }

    .photoFallback {
      width: 100%; height: 100%;
      display: flex; align-items: center; justify-content: center;
      font-size: 72px; font-weight: 700;
      color: var(--text-muted);
      background: var(--bg-elevated);
    }

    .photoGrad {
      position: absolute; bottom: 0; left: 0; right: 0;
      padding: 32px 20px 18px;
      background: linear-gradient(to bottom, transparent 0%, rgba(0,0,0,0.72) 100%);
      display: flex; flex-direction: column; gap: 8px;
    }

    .name {
      font-size: 22px; font-weight: 700;
      color: #fff; letter-spacing: -0.01em;
    }

    .choiceBadge {
      display: inline-flex; align-items: center;
      padding: 4px 12px; border-radius: 9999px;
      font-size: 12px; font-weight: 600; letter-spacing: 0.05em;
      width: fit-content;
    }
    .choiceBadge.magical {
      background: rgba(212,160,23,0.18);
      border: 1px solid rgba(212,160,23,0.45);
      color: var(--gold-300);
    }
    .choiceBadge.logical {
      background: rgba(220,80,80,0.14);
      border: 1px solid rgba(220,80,80,0.38);
      color: var(--rose-300);
    }

    /* ── Writing area ── */
    .writeWrap {
      flex: 1;
      display: flex; flex-direction: column; gap: 12px;
      padding: 18px 20px 24px;
      background: var(--bg-elevated);
      border-top: 1px solid var(--border-subtle);
      overflow-y: auto;
    }

    /* Bridge question */
    .bridgeWrap {
      display: flex; flex-direction: column; gap: 6px;
    }

    .bridgeLabel {
      font-family: var(--font-ui); font-size: 10px; font-weight: 600;
      letter-spacing: 0.08em; text-transform: uppercase;
      color: var(--text-dim);
    }

    .bridgeQuestion {
      display: block; width: 100%;
      padding: 11px 14px;
      background: rgba(255,255,255,0.03);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      color: var(--text-secondary);
      font-family: var(--font-ui); font-size: 14px; line-height: 1.5;
      text-align: left; cursor: pointer;
      transition: border-color 0.15s ease, color 0.15s ease, background 0.15s ease;
    }
    .bridgeQuestion:hover {
      border-color: var(--border-soft);
      color: var(--text-primary);
      background: rgba(255,255,255,0.05);
    }

    /* Starter dropdown */
    .starterWrap { position: relative; }

    .starterTrigger {
      display: flex; align-items: center; justify-content: space-between;
      width: 100%; padding: 9px 14px;
      background: rgba(255,255,255,0.03);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      cursor: pointer;
      transition: border-color 0.15s ease;
    }
    .starterTrigger:hover { border-color: var(--border-soft); }

    .starterLabel {
      font-family: var(--font-ui); font-size: 13px;
      font-weight: 500; color: var(--text-muted);
    }

    .dropArrow {
      font-size: 12px; color: var(--text-dim);
      transition: transform 0.2s ease; display: inline-block;
    }
    .dropArrow.open { transform: rotate(180deg); }

    .starterList {
      position: absolute; top: calc(100% + 5px); left: 0; right: 0; z-index: 20;
      background: var(--bg-surface, var(--bg-elevated));
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-lg);
      overflow: hidden;
      box-shadow: 0 8px 28px rgba(0,0,0,0.35);
    }

    .starterItem {
      display: block; width: 100%;
      padding: 13px 16px;
      background: transparent;
      border: none; border-bottom: 1px solid var(--border-subtle);
      color: var(--text-secondary); font-size: 14px;
      text-align: left; cursor: pointer;
      font-family: var(--font-ui);
      transition: background 0.12s ease, color 0.12s ease;
    }
    .starterItem:last-child { border-bottom: none; }
    .starterItem:hover { background: rgba(255,255,255,0.05); color: var(--text-primary); }

    /* Textarea */
    .noteInput {
      width: 100%; flex: 1;
      padding: 14px; min-height: 90px;
      background: rgba(255,255,255,0.04);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      color: var(--text-primary);
      font-family: var(--font-ui); font-size: 15px; line-height: 1.65;
      resize: none; outline: none;
      transition: border-color 0.2s ease;
      box-sizing: border-box;
    }
    .noteInput::placeholder { color: var(--text-dim); }
    .noteInput:focus { border-color: var(--border-soft); }

    /* Meta row */
    .metaRow {
      display: flex; align-items: center; justify-content: space-between;
      min-height: 16px;
    }
    .minHint {
      font-family: var(--font-ui); font-size: 11px; color: var(--text-dim);
    }
    .charCount {
      font-family: var(--font-data); font-size: 11px; color: var(--text-dim);
      margin-left: auto;
      transition: color 0.2s ease;
    }
    .charCount.warn { color: var(--rose-300); }

    /* Send button */
    .sendBtn {
      width: 100%; padding: 15px 24px;
      border: none; border-radius: var(--radius-xl);
      font-family: var(--font-ui); font-size: 15px; font-weight: 700;
      cursor: pointer;
      transition: opacity 0.2s ease, transform 0.15s ease;
    }
    .sendBtn.magical {
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base);
      box-shadow: 0 4px 20px rgba(212,160,23,0.28);
    }
    .sendBtn.logical {
      background: linear-gradient(135deg, var(--rose-500), var(--rose-400));
      color: #fff;
      box-shadow: 0 4px 20px var(--rose-glow);
    }
    .sendBtn.magical {
      box-shadow: 0 4px 20px var(--gold-glow);
    }
    .sendBtn:hover:not(:disabled) { opacity: 0.88; }
    .sendBtn:active:not(:disabled) { transform: scale(0.96); }
    .sendBtn:disabled { opacity: 0.35; cursor: not-allowed; }
  `],
})
export class ChatNoteOverlayComponent {
  @Input() card: OverlayCard | null = null;
  @Input() choice: 'MAGICAL' | 'LOGICAL' | null = null;
  @Output() back = new EventEmitter<void>();
  @Output() submitted = new EventEmitter<string>();

  starters = STARTERS;
  noteText = '';
  showDropdown = false;
  submitting = false;

  get canSend(): boolean {
    return this.noteText.trim().length >= 20;
  }

  constructor(private cdr: ChangeDetectorRef) {}

  toggleDropdown() {
    this.showDropdown = !this.showDropdown;
    this.cdr.markForCheck();
  }

  applyBridgeQuestion() {
    if (this.card?.bridgeQuestion) {
      this.noteText = this.card.bridgeQuestion;
      this.cdr.markForCheck();
    }
  }

  applyStarter(s: string) {
    this.noteText = s;
    this.showDropdown = false;
    this.cdr.markForCheck();
  }

  onInput() {
    this.cdr.markForCheck();
  }

  onBack() {
    this.noteText = '';
    this.showDropdown = false;
    this.back.emit();
  }

  onSend() {
    if (!this.canSend || this.submitting) return;
    this.submitted.emit(this.noteText.trim());
  }
}
