import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

export const authGuard: CanActivateFn = () => {
  const isBrowser = typeof window !== 'undefined' && !!window.localStorage;
  if (!isBrowser) return true;

  const token = localStorage.getItem('accessToken');
  if (token) return true;

  return inject(Router).parseUrl('/login');
};
