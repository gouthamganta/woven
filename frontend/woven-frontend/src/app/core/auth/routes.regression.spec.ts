import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, Routes, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';

@Component({ standalone: true, imports: [RouterOutlet], template: '<router-outlet />' })
class RouteProbeComponent {}

// Keep real paths, redirects and guard configuration; replace page rendering
// only, so routing security can be tested without provider/network side effects.
function probeRoutes(configuration: Routes): Routes {
  return configuration.map(route => ({ ...route,
    ...(route.component ? { component: RouteProbeComponent } : {}),
    ...(route.children ? { children: probeRoutes(route.children) } : {}),
  }));
}

describe('Actual route configuration session boundaries', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter(probeRoutes(routes))] });
  });
  afterEach(() => localStorage.clear());

  for (const path of ['/login', '/privacy', '/terms', '/data-policy', '/landing']) {
    it(`allows anonymous navigation to public ${path}`, async () => {
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl(path);
      expect(TestBed.inject(Router).url).toBe(path);
    });
  }

  for (const path of [
    '/moments', '/commons', '/chats', '/chats/00000000-0000-0000-0000-000000000001',
    '/you', '/you/settings', '/you/tiles', '/matches/00000000-0000-0000-0000-000000000001/profile',
    '/onboarding/welcome', '/onboarding/basics', '/onboarding/intent', '/onboarding/foundational',
    '/onboarding/photos', '/onboarding/details', '/onboarding/lifestyle', '/onboarding/review', '/onboarding/start',
    '/', '/app', '/missing-qa-page',
  ]) {
    it(`redirects anonymous navigation from protected ${path}`, async () => {
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl(path);
      expect(TestBed.inject(Router).url).toBe('/login');
    });
  }

  it('rechecks expiry when navigating between protected child pages', async () => {
    const token = (exp:number) => `${btoa('{"alg":"HS256"}')}.${btoa(JSON.stringify({exp}))}.synthetic-signature`;
    localStorage.setItem('accessToken', token(Math.floor(Date.now()/1000)+3600));
    const harness=await RouterTestingHarness.create();
    await harness.navigateByUrl('/moments');
    expect(TestBed.inject(Router).url).toBe('/moments');
    localStorage.setItem('accessToken', token(1));
    await harness.navigateByUrl('/chats');
    expect(TestBed.inject(Router).url).toBe('/login');
  });
});
