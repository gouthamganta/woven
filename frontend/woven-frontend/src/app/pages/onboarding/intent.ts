import { Component, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

const INTENTS = [
  { key: 'long_term',        label: 'Something lasting',   sub: 'You\'re here for something real' },
  { key: 'short_term',       label: 'Casual connection',   sub: 'Open, honest, no pressure' },
  { key: 'friendship',       label: 'Friendship first',    sub: 'Connection before anything else' },
  { key: 'open_to_anything', label: 'Open to anything',    sub: 'Let it unfold naturally' },
];

const OPENNESS = [
  { key: 'long_term',         label: 'Long-term' },
  { key: 'short_term',        label: 'Short-term' },
  { key: 'friendship',        label: 'Friendship' },
  { key: 'open_to_anything',  label: 'Open to anything' },
  { key: 'not_sure',          label: 'Still figuring it out' },
];


@Component({
  selector: 'woven-onboarding-intent',
  standalone: true,
  imports: [CommonModule, FormsModule, OnboardingShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <woven-onboarding-shell
      title="What are you here for?"
      subtitle="Honesty here shapes everything that follows."
      [stepNumber]="4"
      [totalSteps]="8"
      stepLabel="Intent"
    >
      <div class="stack">

        <div class="field">
          <label class="label">Your primary intent</label>
          <div class="intentGrid">
            <button *ngFor="let i of intents" class="intentCard" [class.active]="primaryIntent === i.key" (click)="primaryIntent = i.key; mark()">
              <span class="intentLabel">{{ i.label }}</span>
              <span class="intentSub">{{ i.sub }}</span>
            </button>
          </div>
        </div>

        <div class="divider"></div>

        <div class="field">
          <label class="label">Also open to <span class="opt">pick all that apply</span></label>
          <div class="pills">
            <button *ngFor="let o of openness" class="pill" [class.active]="opennessSet.has(o.key)" (click)="toggleOpenness(o.key)">
              {{ o.label }}
            </button>
          </div>
        </div>

        <div class="divider"></div>

        <div class="field">
          <label class="label">What would a meaningful connection look like for you?</label>
          <p class="fieldNote">This shapes how we think about your matches — it's for the engine, not your profile.</p>
          <textarea
            class="textarea"
            [(ngModel)]="reflection"
            placeholder="Write freely. A sentence or two is plenty."
            maxlength="200"
            rows="4"
          ></textarea>
          <div class="charCount" [class.warn]="reflection.length > 170">{{ reflection.length }} / 200</div>
        </div>

        <p class="err" *ngIf="err">{{ err }}</p>

        <button class="cta" (click)="next()" [disabled]="loading || !canProceed">
          <span *ngIf="!loading">Continue →</span>
          <span *ngIf="loading">Saving…</span>
        </button>

      </div>
    </woven-onboarding-shell>
  `,
  styles: [`
    .stack { display: grid; gap: 22px; }
    .divider { height: 1px; background: linear-gradient(90deg, transparent, var(--border-soft), transparent); }
    .field { display: grid; gap: 10px; }
    .label { font-family: var(--font-ui); font-size: 11px; font-weight: 700; letter-spacing: 0.12em; text-transform: uppercase; color: var(--text-muted); }
    .opt { font-weight: 500; opacity: 0.6; text-transform: none; letter-spacing: 0; }
    .fieldNote { font-family: var(--font-ui); font-size: 12px; color: var(--text-dim); line-height: 1.5; margin: -4px 0 0; }

    .intentGrid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .intentCard {
      padding: 14px; background: rgba(255,255,255,0.03); border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg); display: grid; gap: 4px; text-align: left; cursor: pointer;
      transition: all 0.2s ease;
    }
    .intentCard:hover { border-color: var(--border-soft); }
    .intentCard.active { border-color: var(--gold-400); background: rgba(212,160,23,0.08); }
    .intentLabel { font-family: var(--font-ui); font-size: 14px; font-weight: 600; color: var(--text-primary); }
    .intentSub { font-family: var(--font-ui); font-size: 11px; color: var(--text-muted); line-height: 1.4; }
    .intentCard.active .intentLabel { color: var(--gold-300); }

    .pills { display: flex; flex-wrap: wrap; gap: 8px; }
    .pill {
      padding: 9px 16px; border: 1px solid var(--border-subtle); border-radius: 9999px;
      background: transparent; color: var(--text-muted); font-family: var(--font-ui);
      font-size: 13px; font-weight: 500; cursor: pointer; transition: all 0.15s ease;
    }
    .pill:hover { border-color: var(--border-soft); color: var(--text-secondary); }
    .pill.active { border-color: var(--gold-400); background: rgba(212,160,23,0.10); color: var(--gold-300); font-weight: 600; }

    .textarea {
      width: 100%; padding: 14px; background: rgba(255,255,255,0.04);
      border: 1px solid var(--border-subtle); border-radius: var(--radius-lg);
      color: var(--text-primary); font-family: var(--font-ui); font-size: 14px;
      line-height: 1.6; resize: vertical; outline: none; transition: border-color 0.2s ease; box-sizing: border-box;
    }
    .textarea::placeholder { color: var(--text-dim); }
    .textarea:focus { border-color: var(--gold-400); }
    .charCount { font-family: var(--font-data); font-size: 11px; color: var(--text-dim); text-align: right; transition: color 0.2s ease; }
    .charCount.warn { color: var(--rose-300); }

    .cta {
      width: 100%; padding: 16px 24px; border: none; border-radius: var(--radius-xl);
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base); font-family: var(--font-ui); font-size: 15px; font-weight: 700;
      cursor: pointer; transition: opacity 0.2s ease, transform 0.15s ease;
      box-shadow: 0 4px 20px rgba(212,160,23,0.28);
    }
    .cta:hover:not(:disabled) { opacity: 0.88; }
    .cta:active:not(:disabled) { transform: scale(0.96); }
    .cta:disabled { opacity: 0.4; cursor: not-allowed; }
    .err { font-size: 12px; color: var(--rose-300); }
  `],
})
export class IntentOnboardingComponent {
  intents  = INTENTS;
  openness = OPENNESS;

  primaryIntent = '';
  opennessSet   = new Set<string>();
  reflection    = '';
  loading = false;
  err = '';

  get canProceed() { return !!this.primaryIntent && this.reflection.trim().length > 0; }

  constructor(
    private onboarding: OnboardingService,
    private router: Router,
    private cdr: ChangeDetectorRef,
  ) {}

  mark() { this.cdr.markForCheck(); }

  toggleOpenness(key: string) {
    this.opennessSet.has(key) ? this.opennessSet.delete(key) : this.opennessSet.add(key);
    this.cdr.markForCheck();
  }

  async next() {
    if (!this.canProceed) return;
    this.loading = true; this.err = ''; this.cdr.markForCheck();
    try {
      const res = await firstValueFrom(this.onboarding.submitIntent({
        primaryIntent: this.primaryIntent,
        openness: [...this.opennessSet],
        reflectionSentence: this.reflection.trim(),
      }));
      this.router.navigateByUrl(res.nextRoute || '/onboarding/foundational');
    } catch {
      this.err = 'Could not save. Please try again.';
    } finally {
      this.loading = false; this.cdr.markForCheck();
    }
  }
}
