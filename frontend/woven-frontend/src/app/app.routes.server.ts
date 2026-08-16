import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // Unauthenticated — client-only (needs browser APIs)
  { path: 'login',       renderMode: RenderMode.Client },
  { path: 'app',         renderMode: RenderMode.Client },
  { path: 'privacy',     renderMode: RenderMode.Client },
  { path: 'terms',       renderMode: RenderMode.Client },
  { path: 'data-policy', renderMode: RenderMode.Client },

  // Onboarding — client-only (auth-guarded, localStorage)
  { path: 'onboarding/welcome',      renderMode: RenderMode.Client },
  { path: 'onboarding/basics',       renderMode: RenderMode.Client },
  { path: 'onboarding/intent',       renderMode: RenderMode.Client },
  { path: 'onboarding/foundational', renderMode: RenderMode.Client },
  { path: 'onboarding/photos',       renderMode: RenderMode.Client },
  { path: 'onboarding/details',      renderMode: RenderMode.Client },
  { path: 'onboarding/lifestyle',    renderMode: RenderMode.Client },
  { path: 'onboarding/review',       renderMode: RenderMode.Client },
  { path: 'onboarding/start',        renderMode: RenderMode.Client },

  // Main app shell children — all client-only (auth-guarded)
  { path: '',                    renderMode: RenderMode.Client },
  { path: 'moments',             renderMode: RenderMode.Client },
  { path: 'commons',             renderMode: RenderMode.Client },
  { path: 'chats',                        renderMode: RenderMode.Client },
  { path: 'chats/:id',                    renderMode: RenderMode.Client },
  { path: 'matches/:matchId/profile',     renderMode: RenderMode.Client },
  { path: 'you',                          renderMode: RenderMode.Client },
  { path: 'you/settings',                 renderMode: RenderMode.Client },
  { path: 'you/tiles',                    renderMode: RenderMode.Client },

  // Everything else — client-only (backend not available in local dev)
  { path: '**', renderMode: RenderMode.Client },
];
