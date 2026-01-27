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
        .stack{ display:grid; gap:14px; }
        .label{ font-size:12px; opacity:.7; margin-bottom:6px; display:block; }
        .chips{ display:flex; flex-wrap:wrap; gap:10px; }
        .chip{
          padding:10px 12px;
          border-radius:999px;
          border:1px solid rgba(0,0,0,.14);
          background: rgba(255,255,255,.75);
          cursor:pointer;
          font-size:13px;
        }
        .chip.active{
          background:#111;
          color:#fff;
          border-color:#111;
        }
        .helper{ font-size:12px; opacity:.65; line-height:1.4; margin-top:6px; }
        .textarea{
          width:100%;
          min-height: 90px;
          resize: vertical;
          padding: 12px;
          border-radius: 14px;
          border: 1px solid rgba(0,0,0,.18);
          outline: none;
          background: rgba(255,255,255,.85);
        }
        .textarea:focus{ border-color:#111; box-shadow:0 0 0 3px rgba(0,0,0,.08); }
        .row{
          display:flex;
          justify-content:space-between;
          align-items:center;
          gap:12px;
          flex-wrap:wrap;
        }
        .btn{
          width:100%;
          padding:12px 14px;
          border-radius: 14px;
          border:0;
          background:#111;
          color:#fff;
          cursor:pointer;
          font-weight: 600;
        }
        .btn:disabled{ opacity:.6; cursor:not-allowed; }
        .error{ color:#b00020; font-size:13px; margin-top:6px; }
        .mini{ font-size:12px; opacity:.65; }
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

