import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../../environments/environment';

function readStoredToken(): string | null {
  try {
    if (typeof window === 'undefined' || !window.localStorage) return null;
    return window.localStorage.getItem('accessToken');
  } catch {
    return null;
  }
}

function resolveOrigin(url: string, base: string): string | null {
  try {
    const parsed = new URL(url, base);
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null;
    return parsed.origin;
  } catch {
    return null;
  }
}

/** True only for same-origin requests or requests to the configured environment.apiUrl origin. */
export function isAppApiUrl(url: string, pageHref: string, apiUrl: string = environment.apiUrl): boolean {
  const requestOrigin = resolveOrigin(url, pageHref);
  if (!requestOrigin) return false;

  const allowed = new Set<string>();
  const pageOrigin = resolveOrigin(pageHref, pageHref);
  if (pageOrigin) allowed.add(pageOrigin);
  if (apiUrl) {
    const apiOrigin = resolveOrigin(apiUrl, pageHref);
    if (apiOrigin) allowed.add(apiOrigin);
  }

  return allowed.has(requestOrigin);
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // SSR-safe: no window/location on the server, so never attach credentials there
  if (typeof window === 'undefined' || !window.location) return next(req);

  // Leave caller-supplied Authorization headers untouched
  if (req.headers.has('Authorization')) return next(req);

  if (!isAppApiUrl(req.url, window.location.href)) return next(req);

  const token = readStoredToken();
  if (!token || !token.trim()) return next(req);

  const authReq = req.clone({
    setHeaders: { Authorization: `Bearer ${token}` }
  });

  return next(authReq);
};
