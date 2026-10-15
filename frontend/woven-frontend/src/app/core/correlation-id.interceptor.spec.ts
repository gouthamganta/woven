import { HttpRequest, HttpResponse } from '@angular/common/http';
import { of } from 'rxjs';
import { correlationIdInterceptor } from './correlation-id.interceptor';

describe('Request tracing', () => {
  function forwarded(request: HttpRequest<unknown>) {
    let actual: HttpRequest<unknown> | undefined;
    correlationIdInterceptor(request, next => { actual = next; return of(new HttpResponse()); });
    return actual!;
  }

  it('adds a usable trace identifier without changing request data or caller headers', () => {
    const body = { noteText: 'Synthetic note' };
    const request = new HttpRequest('POST', '/moments/choose', body)
      .clone({ setHeaders: { 'X-Idempotency-Key': 'synthetic-retry', Authorization: 'Bearer synthetic' } });
    const actual = forwarded(request);
    expect(actual.headers.get('X-Correlation-ID')).toMatch(/^[0-9a-f]{16}$/);
    expect(actual.headers.get('X-Idempotency-Key')).toBe('synthetic-retry');
    expect(actual.headers.get('Authorization')).toBe('Bearer synthetic');
    expect(actual.body).toBe(body);
    expect(actual.url).toBe(request.url);
    expect(actual.method).toBe('POST');
    expect(request.headers.has('X-Correlation-ID')).toBe(false);
  });

  it('creates separate traces for separate outgoing attempts', () => {
    const request = new HttpRequest('GET', '/matches');
    expect(forwarded(request).headers.get('X-Correlation-ID'))
      .not.toBe(forwarded(request).headers.get('X-Correlation-ID'));
  });
});
