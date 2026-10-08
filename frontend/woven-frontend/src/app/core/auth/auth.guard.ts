import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

const BASE64URL_SEGMENT = /^[A-Za-z0-9_-]+={0,2}$/;

function readStoredToken(): string | null {
  try {
    if (typeof window === 'undefined' || !window.localStorage) return null;
    return window.localStorage.getItem('accessToken');
  } catch {
    return null;
  }
}

function decodeBase64Url(segment: string): string | null {
  try {
    const base64 = segment.replace(/=+$/, '').replace(/-/g, '+').replace(/_/g, '/');
    if (base64.length % 4 === 1) return null;
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const binary = atob(padded);
    const bytes = Uint8Array.from(binary, c => c.charCodeAt(0));
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return null;
  }
}

/**
 * Client-side navigation check only: the token is well-formed and its exp is in the future.
 * This does NOT verify the signature — the server remains the authority on identity.
 */
export function hasUsableSessionToken(token: string | null, nowMs: number = Date.now()): boolean {
  if (typeof token !== 'string') return false;
  const trimmed = token.trim();
  if (!trimmed) return false;

  const parts = trimmed.split('.');
  if (parts.length !== 3 || !parts.every(p => BASE64URL_SEGMENT.test(p))) return false;

  const json = decodeBase64Url(parts[1]);
  if (json === null) return false;

  let payload: unknown;
  try {
    payload = JSON.parse(json);
  } catch {
    return false;
  }
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) return false;

  const exp = (payload as Record<string, unknown>)['exp'];
  if (typeof exp !== 'number' || !Number.isFinite(exp)) return false;

  return exp * 1000 > nowMs;
}

export const authGuard: CanActivateFn = () => {
  // Resolve Router up front while still inside the guard's injection context.
  const router = inject(Router);

  // SSR / no storage: fail closed rather than render protected content without a session.
  if (hasUsableSessionToken(readStoredToken())) return true;

  return router.parseUrl('/login');
};
