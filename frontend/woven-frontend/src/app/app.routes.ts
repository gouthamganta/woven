import { Routes } from '@angular/router';
import { LoginComponent } from './pages/login/login';
import { TestApiComponent } from './test-api/test-api.component';

import { OnboardingGateComponent } from './onboarding/onboarding-gate.component';
import { authGuard } from './core/auth/auth.guard';

import { HomeComponent } from './pages/home/home';
import { ProfilePageComponent } from './pages/profile/profile';
import { SettingsPageComponent } from './pages/settings/settings';

import { MomentsPageComponent } from './pages/moments/moments.page';
import { PendingMomentsPageComponent } from './pages/moments/pending.page';

import { ChatsListComponent } from './pages/chats/chats-list.component';
import { ChatThreadComponent } from './pages/chats/chat-thread.component';
import { MatchProfilePreviewPageComponent } from './pages/matches/match-profile-preview.page';

import { WelcomeOnboardingComponent } from './pages/onboarding/welcome';
import { BasicsOnboardingComponent } from './pages/onboarding/basics';
import { IntentOnboardingComponent } from './pages/onboarding/intent';
import { DetailsOnboardingComponent } from './pages/onboarding/details';
import { ReviewOnboardingComponent } from './pages/onboarding/review';
import { StartOnboardingComponent } from './pages/onboarding/start';
import { FoundationalComponent } from './onboarding/foundational.component';
import { PhotosPageComponent } from './pages/onboarding/photos/photos.page';

export const routes: Routes = [
  { path: '', redirectTo: '/login', pathMatch: 'full' },

  { path: 'login', component: LoginComponent },
  { path: 'app', canActivate: [authGuard], component: OnboardingGateComponent },

  {
    path: 'home',
    component: HomeComponent,
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'moments', pathMatch: 'full' },
      { path: 'moments', component: MomentsPageComponent },
      { path: 'moments/pending', component: PendingMomentsPageComponent },

      { path: 'chats', component: ChatsListComponent },
      { path: 'chats/:threadId', component: ChatThreadComponent },

      { path: 'matches/:matchId/profile', component: MatchProfilePreviewPageComponent },

      { path: 'profile', component: ProfilePageComponent },
    ],
  },

  { path: 'settings', canActivate: [authGuard], component: SettingsPageComponent },

  { path: 'onboarding/welcome', canActivate: [authGuard], component: WelcomeOnboardingComponent },
  { path: 'onboarding/basics', canActivate: [authGuard], component: BasicsOnboardingComponent },
  { path: 'onboarding/intent', canActivate: [authGuard], component: IntentOnboardingComponent },
  { path: 'onboarding/foundational', canActivate: [authGuard], component: FoundationalComponent },
  { path: 'onboarding/photos', canActivate: [authGuard], component: PhotosPageComponent },
  { path: 'onboarding/details', canActivate: [authGuard], component: DetailsOnboardingComponent },
  { path: 'onboarding/review', canActivate: [authGuard], component: ReviewOnboardingComponent },
  { path: 'onboarding/start', canActivate: [authGuard], component: StartOnboardingComponent },

  { path: 'test', component: TestApiComponent },

  // Catch-all route - must be LAST
  { path: '**', redirectTo: '/login' },
];