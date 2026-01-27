import { Routes } from '@angular/router';
import { LoginComponent } from './pages/login/login';
import { TestApiComponent } from './test-api/test-api.component';

import { OnboardingGateComponent } from './onboarding/onboarding-gate.component';

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
  { path: 'app', component: OnboardingGateComponent },

  // Home becomes the shell; all app pages live under /home/...
  {
    path: 'home',
    component: HomeComponent,
    children: [
      { path: '', redirectTo: 'moments', pathMatch: 'full' },
      { path: 'moments', component: MomentsPageComponent },
      { path: 'moments/pending', component: PendingMomentsPageComponent },

      { path: 'chats', component: ChatsListComponent },
      { path: 'chats/:threadId', component: ChatThreadComponent },

      { path: 'matches/:matchId/profile', component: MatchProfilePreviewPageComponent },

      // optional: if you want profile inside the shell
      { path: 'profile', component: ProfilePageComponent },
    ],
  },

  // keep these standalone (fine)
  { path: 'settings', component: SettingsPageComponent },

  { path: 'onboarding/welcome', component: WelcomeOnboardingComponent },
  { path: 'onboarding/basics', component: BasicsOnboardingComponent },
  { path: 'onboarding/intent', component: IntentOnboardingComponent },
  { path: 'onboarding/foundational', component: FoundationalComponent },
  { path: 'onboarding/photos', component: PhotosPageComponent },
  { path: 'onboarding/details', component: DetailsOnboardingComponent },
  { path: 'onboarding/review', component: ReviewOnboardingComponent },
  { path: 'onboarding/start', component: StartOnboardingComponent },

  { path: 'test', component: TestApiComponent },
];