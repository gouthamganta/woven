import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { OnboardingService, OnboardingStateResponse } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

@Component({
  standalone: true,
  imports: [CommonModule, OnboardingShellComponent],
  template: `
    <woven-onboarding-shell
      title="Let’s start"
      subtitle="A few quick steps. You can resume anytime."
      [stepNumber]="1"
      [totalSteps]="6"
      stepLabel="Onboarding"
    >
      <style>
        .startContent {
          display: flex;
          flex-direction: column;
          gap: 16px;
        }

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

        .error {
          margin-top: 4px;
          padding: 12px 14px;
          border-radius: 12px;
          background: rgba(176, 0, 32, 0.06);
          border: 1px solid rgba(176, 0, 32, 0.12);
          color: #b00020;
          font-size: 13px;
          font-weight: 500;
          line-height: 1.4;
        }
      </style>

      <div class="startContent">
        <button class="btn" type="button" (click)="continue()" [disabled]="loading">
          {{ loading ? 'Continuing...' : 'Continue' }}
        </button>

        <div *ngIf="error" class="error">{{ error }}</div>
      </div>
    </woven-onboarding-shell>
  `
})
export class StartOnboardingComponent {
  loading = false;
  error = '';

  constructor(
    private http: HttpClient,
    private onboarding: OnboardingService,
    private router: Router
  ) {}

  async continue() {
    this.error = '';
    this.loading = true;

    try {
      // 1) mark welcome step done
      await firstValueFrom(
        this.http.post(`${environment.apiUrl}/onboarding/welcome`, {})
      );

      // 2) ask backend where to go next
      const state: OnboardingStateResponse = await firstValueFrom(this.onboarding.getState());

      // 3) route to next step
      this.router.navigateByUrl(state.nextRoute);
    } catch (e) {
      this.error = 'Something went wrong. Please try again.';
    } finally {
      this.loading = false;
    }
  }
}



