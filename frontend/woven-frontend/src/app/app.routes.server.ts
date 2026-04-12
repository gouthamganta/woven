import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // ✅ Public pages can be server-rendered if you want
  // { path: '', renderMode: RenderMode.Server },

  // ✅ Auth/onboarding should be client-only (needs localStorage token)
  { path: 'login', renderMode: RenderMode.Client },
  { path: 'app', renderMode: RenderMode.Client },

  // ✅ Home and its children are auth-guarded — client-only
  { path: 'home', renderMode: RenderMode.Client },
  { path: 'home/moments', renderMode: RenderMode.Client },
  { path: 'home/moments/pending', renderMode: RenderMode.Client },
  { path: 'home/chats', renderMode: RenderMode.Client },
  { path: 'home/chats/:threadId', renderMode: RenderMode.Client },
  { path: 'home/matches/:matchId/profile', renderMode: RenderMode.Client },
  { path: 'home/profile', renderMode: RenderMode.Client },

  { path: 'settings', renderMode: RenderMode.Client },

  // ✅ Onboarding routes listed explicitly — wildcard path matching is
  //    unreliable for flat routes and caused a 405 + hydration failure on intent.
  { path: 'onboarding/welcome', renderMode: RenderMode.Client },
  { path: 'onboarding/basics', renderMode: RenderMode.Client },
  { path: 'onboarding/intent', renderMode: RenderMode.Client },
  { path: 'onboarding/foundational', renderMode: RenderMode.Client },
  { path: 'onboarding/photos', renderMode: RenderMode.Client },
  { path: 'onboarding/details', renderMode: RenderMode.Client },
  { path: 'onboarding/review', renderMode: RenderMode.Client },
  { path: 'onboarding/start', renderMode: RenderMode.Client },

  // ✅ Everything else
  { path: '**', renderMode: RenderMode.Server }
];

