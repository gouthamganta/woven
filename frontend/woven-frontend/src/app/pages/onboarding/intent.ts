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
          font-family: "DM Sans", system-ui, sans-serif;
          font-size: 12px;
          font-weight: 600;
          color: rgba(255, 215, 235, 0.55);
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
          border: 1px solid rgba(255, 255, 255, 0.12);
          background: rgba(255, 255, 255, 0.06);
          cursor: pointer;
          font-family: "DM Sans", system-ui, sans-serif;
          font-size: 13px;
          font-weight: 600;
          color: rgba(255, 245, 250, 0.85);
          transition: all 0.2s ease;
        }

        .chip:hover:not(.active) {
          border-color: rgba(255, 255, 255, 0.22);
          background: rgba(255, 255, 255, 0.10);
        }

        .chip.active {
          background: linear-gradient(135deg, #E05490, #7D5BD0);
          color: #fff;
          border-color: transparent;
          box-shadow: 0 4px 16px rgba(224, 84, 144, 0.30);
        }

        .chip:focus-visible {
          outline: 2px solid #E05490;
          outline-offset: 2px;
        }

        /* ===== Helper Text ===== */
        .helper {
          font-size: 12px;
          font-weight: 450;
          color: rgba(255, 215, 235, 0.46);
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
          border: 1px solid rgba(255, 255, 255, 0.12);
          outline: none;
          background: rgba(44, 29, 53, 0.75);
          font-family: "DM Sans", system-ui, sans-serif;
          font-size: 14px;
          font-weight: 500;
          color: rgba(255, 245, 250, 0.92);
          line-height: 1.5;
          transition: border-color 0.2s ease, box-shadow 0.2s ease;
        }

        .textarea::placeholder {
          color: rgba(255, 215, 235, 0.35);
        }

        .textarea:focus {
          border-color: #E05490;
          box-shadow: 0 0 0 3px rgba(224, 84, 144, 0.28);
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
          font-family: "JetBrains Mono", monospace;
          font-size: 11px;
          font-weight: 500;
          color: rgba(255, 215, 235, 0.40);
        }

        /* ===== Primary Button ===== */
        .btn {
          width: 100%;
          padding: 14px 18px;
          border-radius: 14px;
          border: 0;
          background: linear-gradient(135deg, #E05490, #7D5BD0);
          color: #fff;
          cursor: pointer;
          font-family: "DM Sans", system-ui, sans-serif;
          font-weight: 600;
          font-size: 15px;
          letter-spacing: -0.01em;
          transition: transform 220ms cubic-bezier(0.34, 1.56, 0.64, 1),
                      box-shadow 380ms cubic-bezier(0.16, 1, 0.3, 1);
          box-shadow: 0 8px 32px rgba(224, 84, 144, 0.28);
        }

        .btn:hover:not(:disabled) {
          transform: translateY(-2px) scale(1.01);
          box-shadow: 0 12px 40px rgba(224, 84, 144, 0.38);
        }

        .btn:active:not(:disabled) {
          transform: translateY(0) scale(0.99);
        }

        .btn:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }

        .btn:focus-visible {
          outline: 2px solid #E05490;
          outline-offset: 2px;
        }

        /* ===== Error ===== */
        .error {
          padding: 12px 14px;
          border-radius: 12px;
          background: rgba(255, 112, 112, 0.10);
          border: 1px solid rgba(255, 112, 112, 0.22);
          color: #ff7070;
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

