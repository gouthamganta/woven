import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      // 401 - token expired, redirect to login
      if (error.status === 401) {
        if (typeof window !== 'undefined') {
          // Browser storage can be denied (SecurityError); cleanup must never
          // block login navigation or replace the original HTTP error.
          try {
            localStorage.removeItem('accessToken');
            localStorage.removeItem('user');
          } catch {
            // ignore - redirect to login still proceeds
          }
        }
        router.navigateByUrl('/login');
      }

      return throwError(() => error);
    })
  );
};
