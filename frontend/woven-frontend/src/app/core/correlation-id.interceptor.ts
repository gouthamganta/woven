import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Attaches X-Correlation-ID header to all outgoing HTTP requests.
 * Generates a 16-char hex ID per request for end-to-end tracing.
 */
export const correlationIdInterceptor: HttpInterceptorFn = (req, next) => {
  const correlationId = generateCorrelationId();

  const clonedReq = req.clone({
    setHeaders: {
      'X-Correlation-ID': correlationId
    }
  });

  return next(clonedReq);
};

function generateCorrelationId(): string {
  const bytes = new Uint8Array(8);
  crypto.getRandomValues(bytes);
  return Array.from(bytes)
    .map(b => b.toString(16).padStart(2, '0'))
    .join('');
}
