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
        .btn {
          width: 100%;
          padding: 12px 14px;
          border-radius: 14px;
          border: 0;
          background: #111;
          color: #fff;
          cursor: pointer;
          font-weight: 600;
        }
        .btn:disabled { opacity: .6; cursor: not-allowed; }
        .error { margin-top: 12px; color: #b00020; font-size: 13px; }
      </style>

      <button class="btn" type="button" (click)="continue()" [disabled]="loading">
        {{ loading ? 'Continuing…' : 'Continue' }}
      </button>

      <div *ngIf="error" class="error">{{ error }}</div>
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



