import { HttpRequest, HttpResponse } from '@angular/common/http';
import { of } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { environment } from '../../../environments/environment';
import { vi } from 'vitest';

describe('Authentication request boundaries', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => { vi.restoreAllMocks(); localStorage.clear(); });

  function forwarded(request: HttpRequest<unknown>) {
    let actual: HttpRequest<unknown> | undefined;
    authInterceptor(request, next => {
      actual = next;
      return of(new HttpResponse({ status: 200 }));
    });
    return actual!;
  }

  it('forwards an anonymous request without an authorization header', () => {
    const request = new HttpRequest('GET', '/me/data-summary');
    const actual = forwarded(request);
    expect(actual).toBe(request);
    expect(actual.headers.has('Authorization')).toBe(false);
  });

  it('adds the current token to a local API request', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    expect(forwarded(new HttpRequest('GET', '/me/data-summary')).headers.get('Authorization')).toBe('Bearer synthetic-token');
  });

  it('preserves the request body and caller headers when authenticating', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    const body = { noteText: 'Synthetic note' };
    const request = new HttpRequest('POST', '/moments/choose', body, { headers: undefined });
    const actual = forwarded(request.clone({ setHeaders: { 'X-Idempotency-Key': 'synthetic-request' } }));
    expect(actual.body).toBe(body);
    expect(actual.method).toBe('POST');
    expect(actual.headers.get('X-Idempotency-Key')).toBe('synthetic-request');
    expect(request.headers.has('Authorization')).toBe(false);
  });

  it('reads token removal before the next request', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    forwarded(new HttpRequest('GET', '/matches'));
    localStorage.removeItem('accessToken');
    expect(forwarded(new HttpRequest('GET', '/matches')).headers.has('Authorization')).toBe(false);
  });

  it('authenticates an absolute same-origin API URL', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    expect(forwarded(new HttpRequest('GET', new URL('/matches', location.href).href)).headers.get('Authorization')).toBe('Bearer synthetic-token');
  });

  it('authenticates the explicitly configured development API origin', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    const base = environment.apiUrl || location.origin;
    expect(forwarded(new HttpRequest('GET', `${base}/matches`)).headers.get('Authorization')).toBe('Bearer synthetic-token');
  });

  it('does not authenticate a protocol-relative unrelated host', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    expect(forwarded(new HttpRequest('GET', '//untrusted.example.invalid/matches')).headers.has('Authorization')).toBe(false);
  });

  it('does not authenticate an allowed-origin lookalike', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    expect(forwarded(new HttpRequest('GET', 'http://localhost.example.invalid/matches')).headers.has('Authorization')).toBe(false);
  });

  it('preserves unrelated caller-supplied authorization', () => {
    localStorage.setItem('accessToken', 'synthetic-token');
    const request = new HttpRequest('GET', 'https://untrusted.example.invalid/api').clone({setHeaders:{Authorization:'Basic synthetic-provider-auth'}});
    expect(forwarded(request).headers.get('Authorization')).toBe('Basic synthetic-provider-auth');
  });

  it('continues without adding credentials when storage is unavailable', () => {
    vi.spyOn(Storage.prototype,'getItem').mockImplementation(() => { throw new Error('Storage unavailable'); });
    expect(forwarded(new HttpRequest('GET', '/matches')).headers.has('Authorization')).toBe(false);
  });
});
