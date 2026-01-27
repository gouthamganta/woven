import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from './onboarding.service';

@Component({
  standalone: true,
  template: `<div style="padding:24px; opacity:.7;">Loading...</div>`
})
export class OnboardingGateComponent implements OnInit {
  constructor(private onboarding: OnboardingService, private router: Router) {}

  async ngOnInit() {
    const isBrowser = typeof window !== 'undefined' && !!window.localStorage;

    // If SSR is rendering, don't run routing logic here
    if (!isBrowser) return;

    try {
      const state = await firstValueFrom(this.onboarding.getState());

      if (state.profileStatus === 'COMPLETE') {
        this.router.navigateByUrl('/home');
        return;
      }

      this.router.navigateByUrl(state.nextRoute || '/onboarding/start');
    } catch {
      // Safe localStorage usage
      localStorage.removeItem('accessToken');
      localStorage.removeItem('user');
      this.router.navigateByUrl('/login');
    }
  }
}
