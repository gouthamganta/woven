import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { OnboardingService, OnboardingStateResponse } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

type Intent = 'dating' | 'relationship' | 'marriage' | 'casual' | 'not_sure';

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, OnboardingShellComponent],
  template: `
    <woven-onboarding-shell
      title="Intent"
      subtitle="This isn’t permanent. It just helps us match with less noise."
      [stepNumber]="3"
      [totalSteps]="6"
      stepLabel="Profile"
    >
      <style>
        /* ===== Layout ===== */
        .stack {
          display: grid;
          gap: 24px;
        }

        /* ===== Labels ===== */
        .label {
          font-size: 12px;
          font-weight: 600;
          color: #0f0f0f;
          opacity: 0.65;
          margin-bottom: 10px;
          display: block;
          letter-spacing: 0.01em;
        }

        /* ===== Chips ===== */
        .chips {
          display: flex;
          flex-wrap: wrap;
          gap: 8px;
        }

        .chip {
          padding: 12px 18px;
          min-height: 44px;
          border-radius: 100px;
          border: 1px solid rgba(0, 0, 0, 0.1);
          background: rgba(255, 255, 255, 0.8);
          cursor: pointer;
          font-size: 13px;
          font-weight: 600;
          color: #0f0f0f;
          transition: all 0.2s ease;
        }

        .chip:hover:not(.active) {
          border-color: rgba(0, 0, 0, 0.2);
          background: rgba(255, 255, 255, 1);
        }

        .chip.active {
          background: linear-gradient(135deg, #0f0f0f 0%, #1a1a1a 100%);
          color: #fff;
          border-color: transparent;
          box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
        }

        .chip:focus-visible {
          outline: 2px solid #0f0f0f;
          outline-offset: 2px;
        }

        /* ===== Helper Text ===== */
        .helper {
          font-size: 12px;
          font-weight: 450;
          color: #0f0f0f;
          opacity: 0.55;
          line-height: 1.45;
          margin-top: 10px;
        }

        /* ===== Textarea ===== */
        .textarea {
          width: 100%;
          min-height: 100px;
          resize: vertical;
          padding: 14px;
          border-radius: 14px;
          border: 1px solid rgba(0, 0, 0, 0.1);
          outline: none;
          background: rgba(255, 255, 255, 0.9);
          font-size: 14px;
          font-weight: 500;
          color: #0f0f0f;
          line-height: 1.5;
          transition: all 0.2s ease;
          font-family: inherit;
        }

        .textarea::placeholder {
          color: #0f0f0f;
          opacity: 0.4;
        }

        .textarea:focus {
          border-color: #0f0f0f;
          box-shadow: 0 0 0 3px rgba(15, 15, 15, 0.06);
        }

        /* ===== Row ===== */
        .row {
          display: flex;
          justify-content: space-between;
          align-items: center;
          gap: 12px;
          flex-wrap: wrap;
          margin-top: 8px;
        }

        /* ===== Mini Text ===== */
        .mini {
          font-size: 11px;
          font-weight: 600;
          color: #0f0f0f;
          opacity: 0.45;
        }

        /* ===== Primary Button ===== */
        .btn {
          width: 100%;
          padding: 14px 18px;
          border-radius: 14px;
          border: 0;
          background: linear-gradient(135deg, #0f0f0f 0%, #1a1a1a 100%);
          color: #fff;
          cursor: pointer;
          font-weight: 650;
          font-size: 15px;
          letter-spacing: -0.01em;
          transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
          box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
        }

        .btn:hover:not(:disabled) {
          transform: translateY(-1px);
          box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
        }

        .btn:active:not(:disabled) {
          transform: translateY(0);
        }

        .btn:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }

        .btn:focus-visible {
          outline: 2px solid #0f0f0f;
          outline-offset: 2px;
        }

        /* ===== Error ===== */
        .error {
          padding: 12px 14px;
          border-radius: 12px;
          background: rgba(176, 0, 32, 0.06);
          border: 1px solid rgba(176, 0, 32, 0.12);
          color: #b00020;
          font-size: 13px;
          font-weight: 500;
          line-height: 1.4;
        }

        /* ===== Mobile ===== */
        @media (max-width: 480px) {
          .chips {
            gap: 8px;
          }

          .chip {
            padding: 12px 16px;
            min-height: 44px;
            font-size: 13px;
          }
        }
      </style>

      <div class="stack">
        <!-- Primary intent -->
        <div>
          <label class="label">Primary intent</label>
          <div class="chips">
            <button class="chip" type="button" [class.active]="model.primaryIntent==='relationship'" (click)="model.primaryIntent='relationship'">Relationship</button>
            <button class="chip" type="button" [class.active]="model.primaryIntent==='dating'" (click)="model.primaryIntent='dating'">Dating</button>
            <button class="chip" type="button" [class.active]="model.primaryIntent==='marriage'" (click)="model.primaryIntent='marriage'">Marriage</button>
            <button class="chip" type="button" [class.active]="model.primaryIntent==='casual'" (click)="model.primaryIntent='casual'">Casual</button>
            <button class="chip" type="button" [class.active]="model.primaryIntent==='not_sure'" (click)="model.primaryIntent='not_sure'">Not sure</button>
          </div>
          <div class="helper">Pick the closest one. You can adjust later.</div>
        </div>

        <!-- Openness -->
        <div>
          <label class="label">Open to</label>
          <div class="chips">
            <button class="chip" type="button" [class.active]="hasOpen('long_term')" (click)="toggleOpen('long_term')">Long-term</button>
            <button class="chip" type="button" [class.active]="hasOpen('short_term')" (click)="toggleOpen('short_term')">Short-term</button>
            <button class="chip" type="button" [class.active]="hasOpen('friendship')" (click)="toggleOpen('friendship')">Friendship</button>
            <button class="chip" type="button" [class.active]="hasOpen('exploring')" (click)="toggleOpen('exploring')">Exploring</button>
          </div>
          <div class="helper">This helps us avoid mismatched expectations.</div>
        </div>

        <!-- Reflection sentence -->
        <div>
          <label class="label">One sentence about what you want</label>
          <textarea
            class="textarea"
            [(ngModel)]="model.reflectionSentence"
            placeholder="Example: I want something calm, real, and consistent."></textarea>
          <div class="row">
            <div class="mini">{{ model.reflectionSentence?.length || 0 }}/160</div>
            <div class="mini">Keep it honest. No pressure.</div>
          </div>
        </div>

        <div *ngIf="error" class="error">{{ error }}</div>

        <button class="btn" type="button" [disabled]="saving" (click)="submit()">
          {{ saving ? 'Saving…' : 'Continue' }}
        </button>
      </div>
    </woven-onboarding-shell>
  `
})
export class IntentOnboardingComponent {
  saving = false;
  error = '';

  model: {
    primaryIntent: Intent | '';
    openness: string[];
    reflectionSentence: string;
  } = {
    primaryIntent: '',
    openness: [],
    reflectionSentence: ''
  };

  constructor(
    private http: HttpClient,
    private onboarding: OnboardingService,
    private router: Router
  ) {}

  hasOpen(v: string) {
    return this.model.openness.includes(v);
  }

  toggleOpen(v: string) {
    const set = new Set(this.model.openness);
    set.has(v) ? set.delete(v) : set.add(v);
    this.model.openness = Array.from(set);
  }

  private validate(): string | null {
    if (!this.model.primaryIntent) return 'Please choose a primary intent.';
    if (!this.model.openness.length) return 'Pick at least one “open to”.';
    const text = (this.model.reflectionSentence || '').trim();
    if (!text) return 'Add one sentence about what you want.';
    if (text.length > 160) return 'Keep the sentence under 160 characters.';
    return null;
  }

  async submit() {
    const err = this.validate();
    if (err) { this.error = err; return; }

    this.error = '';
    this.saving = true;

    try {
      await firstValueFrom(
        this.http.put(`${environment.apiUrl}/onboarding/intent`, {
          primaryIntent: this.model.primaryIntent,
          openness: this.model.openness,
          reflectionSentence: this.model.reflectionSentence.trim()
        })
      );

      const state: OnboardingStateResponse = await firstValueFrom(this.onboarding.getState());
      this.router.navigateByUrl(state.nextRoute);
    } catch {
      this.error = 'Could not save intent. Please try again.';
    } finally {
      this.saving = false;
    }
  }
}

