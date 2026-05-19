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
          box-shadow: 0 12px 40px rgba(224, 84, 144, 0.38), 0 4px 16px rgba(125, 91, 208, 0.26);
        }

        .btn:active:not(:disabled) {
          transform: translateY(0) scale(0.99);
        }

        .btn:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }

        .error {
          margin-top: 4px;
          padding: 12px 14px;
          border-radius: 12px;
          background: rgba(255, 112, 112, 0.10);
          border: 1px solid rgba(255, 112, 112, 0.22);
          color: #ff7070;
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



