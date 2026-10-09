import { HttpErrorResponse, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { firstValueFrom, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { httpErrorInterceptor } from './http-error.interceptor';

describe('HTTP failure and session recovery', () => {
  const navigateByUrl = vi.fn().mockResolvedValue(true);
  beforeEach(() => {
    localStorage.clear();
    navigateByUrl.mockClear();
    TestBed.configureTestingModule({ providers: [{ provide: Router, useValue: { navigateByUrl } }] });
  });
  afterEach(() => { vi.restoreAllMocks(); localStorage.clear(); });

  function failed(error: HttpErrorResponse) {
    return firstValueFrom(TestBed.runInInjectionContext(() =>
      httpErrorInterceptor(new HttpRequest('GET', '/matches'), () => throwError(() => error))));
  }

  it('clears expired credentials and navigates to login while preserving the server failure', async () => {
    localStorage.setItem('accessToken', 'synthetic-expired-token');
    localStorage.setItem('user', '{"id":1}');
    localStorage.setItem('theme', 'dark');
    const error = new HttpErrorResponse({ status: 401, error: { correlationId: 'synthetic-trace' } });
    await expect(failed(error)).rejects.toBe(error);
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(localStorage.getItem('user')).toBeNull();
    expect(localStorage.getItem('theme')).toBe('dark');
    expect(navigateByUrl).toHaveBeenCalledWith('/login');
  });

  for (const status of [0, 403, 409, 429, 500, 503]) {
    it(`preserves the session and original diagnostic on HTTP ${status}`, async () => {
      localStorage.setItem('accessToken', 'synthetic-valid-token');
      const error = new HttpErrorResponse({ status, error: { correlationId: 'synthetic-trace' } });
      await expect(failed(error)).rejects.toBe(error);
      expect(localStorage.getItem('accessToken')).toBe('synthetic-valid-token');
      expect(navigateByUrl).not.toHaveBeenCalled();
    });
  }

  it('passes a successful response through without logging the user out', async () => {
    const response = new HttpResponse({ status: 200, body: { matched: true } });
    const result = await firstValueFrom(TestBed.runInInjectionContext(() =>
      httpErrorInterceptor(new HttpRequest('GET', '/matches'), () => of(response))));
    expect(result).toBe(response);
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('still redirects and preserves the 401 when browser storage denies cleanup', async () => {
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new DOMException('Storage disabled', 'SecurityError');
    });
    const error = new HttpErrorResponse({ status: 401, error: { correlationId: 'synthetic-trace' } });
    await expect(failed(error)).rejects.toBe(error);
    expect(navigateByUrl).toHaveBeenCalledWith('/login');
  });
});
