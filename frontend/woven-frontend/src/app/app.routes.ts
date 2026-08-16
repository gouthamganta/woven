import { Routes } from '@angular/router';
import { LoginComponent } from './pages/login/login';
import { LegalComponent } from './pages/legal/legal.component';
import { LandingSimpleComponent } from './pages/landing-simple/landing-simple.component';
import { authGuard } from './core/auth/auth.guard';

import { OnboardingGateComponent } from './onboarding/onboarding-gate.component';
import { HomeComponent } from './pages/home/home';

import { MomentsPageComponent } from './pages/moments/moments.page';
import { CommonsPageComponent } from './pages/commons/commons.page';
import { ChatsListComponent } from './pages/chats/chats-list.component';
import { ChatThreadComponent } from './pages/chats/chat-thread.component';
import { MatchProfilePreviewPageComponent } from './pages/matches/match-profile-preview.page';
import { ProfilePageComponent } from './pages/profile/profile';
import { SettingsPageComponent } from './pages/settings/settings';
import { MyTilesPageComponent } from './pages/my-tiles/my-tiles.page';

import { WelcomeOnboardingComponent } from './pages/onboarding/welcome';
import { BasicsOnboardingComponent } from './pages/onboarding/basics';
import { IntentOnboardingComponent } from './pages/onboarding/intent';
import { DetailsOnboardingComponent } from './pages/onboarding/details';
import { LifestyleOnboardingComponent } from './pages/onboarding/lifestyle';
import { ReviewOnboardingComponent } from './pages/onboarding/review';
import { StartOnboardingComponent } from './pages/onboarding/start';
import { FoundationalComponent } from './onboarding/foundational.component';
import { PhotosPageComponent } from './pages/onboarding/photos/photos.page';

export const routes: Routes = [
  // Landing page (public) — exact match takes precedence
  { path: '', component: LandingSimpleComponent, pathMatch: 'full' },

  // Post-login gate — checks onboarding status, redirects accordingly
  { path: 'app', canActivate: [authGuard], component: OnboardingGateComponent },

  // Unauthenticated
  { path: 'login',       component: LoginComponent },
  { path: 'privacy',     component: LegalComponent },
  { path: 'terms',       component: LegalComponent },
  { path: 'data-policy', component: LegalComponent },

  // Onboarding steps (auth required, rendered outside shell)
  { path: 'onboarding/welcome',     canActivate: [authGuard], component: WelcomeOnboardingComponent },
  { path: 'onboarding/basics',      canActivate: [authGuard], component: BasicsOnboardingComponent },
  { path: 'onboarding/intent',      canActivate: [authGuard], component: IntentOnboardingComponent },
  { path: 'onboarding/foundational',canActivate: [authGuard], component: FoundationalComponent },
  { path: 'onboarding/photos',      canActivate: [authGuard], component: PhotosPageComponent },
  { path: 'onboarding/details',     canActivate: [authGuard], component: DetailsOnboardingComponent },
  { path: 'onboarding/lifestyle',   canActivate: [authGuard], component: LifestyleOnboardingComponent },
  { path: 'onboarding/review',      canActivate: [authGuard], component: ReviewOnboardingComponent },
  { path: 'onboarding/start',       canActivate: [authGuard], component: StartOnboardingComponent },

  // Main app shell (auth required) — wraps moments/commons/chats/you with navigation
  {
    path: '',
    component: HomeComponent,
    canActivate: [authGuard],
    children: [
      { path: 'moments',                     component: MomentsPageComponent },
      { path: 'commons',                     component: CommonsPageComponent },
      { path: 'chats',                       component: ChatsListComponent },
      { path: 'chats/:threadId',             component: ChatThreadComponent },
      { path: 'matches/:matchId/profile',    component: MatchProfilePreviewPageComponent },
      { path: 'you',                         component: ProfilePageComponent },
      { path: 'you/settings',                component: SettingsPageComponent },
      { path: 'you/tiles',                   component: MyTilesPageComponent },
    ],
  },

  // Catch-all
  { path: '**', redirectTo: '' },
];
