import { ErrorHandler, Injectable } from '@angular/core';

@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  handleError(error: any): void {
    // Log to console for debugging
    console.error('Global error:', error);

    // In production, you could send to error tracking service (Sentry, etc)
    // For MVP, just console logging is fine
  }
}
