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
        localStorage.removeItem('accessToken');
        localStorage.removeItem('user');
        router.navigateByUrl('/login');
      }

      return throwError(() => error);
    })
  );
};
