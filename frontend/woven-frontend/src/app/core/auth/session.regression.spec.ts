import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, provideRouter } from '@angular/router';
import { HttpRequest, HttpResponse } from '@angular/common/http';
import { of } from 'rxjs';
import { authGuard } from './auth.guard';
import { authInterceptor } from './auth.interceptor';
import { vi } from 'vitest';

// Deliberate must-pass security contracts. Failures stay visible until the
// application is fixed; presence-only guard tests do not establish session safety.
describe('Session security regressions', () => {
  beforeEach(() => { localStorage.clear(); TestBed.configureTestingModule({ providers: [provideRouter([])] }); });
  afterEach(() => { vi.restoreAllMocks(); localStorage.clear(); });

  function jwt(exp: number) {
    return `${btoa('{"alg":"HS256","typ":"JWT"}')}.${btoa(JSON.stringify({ exp }))}.synthetic-signature`;
  }
  function navigate() { return TestBed.runInInjectionContext(() => authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)); }

  it('allows a browser with a future-dated synthetic token payload', () => {
    localStorage.setItem('accessToken', jwt(Math.floor(Date.now() / 1000) + 3600));
    expect(navigate()).toBe(true);
  });

  it('rejects a token exactly at its expiry second', () => {
    localStorage.setItem('accessToken', jwt(Math.floor(Date.now()/1000)));
    expect(String(navigate())).toBe('/login');
  });

  it('rejects a string-valued numeric expiry', () => {
    const payload=btoa(JSON.stringify({exp:String(Math.floor(Date.now()/1000)+3600)}));
    localStorage.setItem('accessToken', `${btoa('{"alg":"HS256"}')}.${payload}.synthetic-signature`);
    expect(String(navigate())).toBe('/login');
  });

  it('redirects instead of throwing when storage is unavailable', () => {
    vi.spyOn(Storage.prototype,'getItem').mockImplementation(() => { throw new Error('Storage unavailable'); });
    expect(String(navigate())).toBe('/login');
  });

  for (const [name, token] of [
    ['expired token', jwt(1)],
    ['malformed token', 'not-a-jwt'],
    ['whitespace token', '   '],
    ['missing-expiry token', `${btoa('{"alg":"HS256"}')}.${btoa('{}')}.synthetic-signature`],
  ]) {
    it(`redirects a browser with a ${name} to login`, () => {
      localStorage.setItem('accessToken', token);
      expect(String(navigate())).toBe('/login');
    });
  }

  it('does not forward app credentials to an unrelated external origin', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    let forwarded: HttpRequest<unknown> | undefined;
    authInterceptor(new HttpRequest('GET', 'https://untrusted.example.invalid/resource'), request => {
      forwarded = request; return of(new HttpResponse({ status: 200 }));
    });
    expect(forwarded!.headers.has('Authorization')).toBe(false);
  });
});
