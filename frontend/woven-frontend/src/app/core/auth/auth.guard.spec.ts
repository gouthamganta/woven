import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';

describe('Authentication navigation boundaries', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });
  afterEach(() => localStorage.clear());

  function navigate() {
    return TestBed.runInInjectionContext(() => authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));
  }

  it('redirects an anonymous browser to login', () => {
    const result = navigate();
    expect(result instanceof UrlTree).toBe(true);
    expect(String(result)).toBe('/login');
  });

  it('treats an empty stored token as anonymous', () => {
    localStorage.setItem('accessToken', '');
    expect(String(navigate())).toBe('/login');
  });

  it('redirects after the stored token has been removed', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    localStorage.removeItem('accessToken');
    expect(String(navigate())).toBe('/login');
  });
});
