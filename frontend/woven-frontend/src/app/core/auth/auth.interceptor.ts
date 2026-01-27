import { HttpInterceptorFn } from '@angular/common/http';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // SSR-safe: localStorage exists only in browser
  const isBrowser = typeof window !== 'undefined' && !!window.localStorage;

  if (!isBrowser) return next(req);

  const token = localStorage.getItem('accessToken');
  if (!token) return next(req);

  const authReq = req.clone({
    setHeaders: { Authorization: `Bearer ${token}` }
  });

  return next(authReq);
};
