import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // ✅ Public pages can be server-rendered if you want
  // { path: '', renderMode: RenderMode.Server },

  // ✅ Auth/onboarding should be client-only (needs localStorage token)
  { path: 'login', renderMode: RenderMode.Client },
  { path: 'app', renderMode: RenderMode.Client },
  { path: 'home', renderMode: RenderMode.Client },

  { path: 'onboarding/**', renderMode: RenderMode.Client },

  // ✅ Everything else
  { path: '**', renderMode: RenderMode.Server }
];

