import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { Observable } from 'rxjs';

export type CoachingSummary = {
  id: number;
  summaryText: string;
  deliveredAt: string;
  weekStart: string;
};

@Injectable({ providedIn: 'root' })
export class CoachingService {
  constructor(private http: HttpClient) {}

  getCurrentSummary(): Observable<CoachingSummary | null> {
    return this.http.get<CoachingSummary>(`${environment.apiUrl}/coaching/current-summary`);
  }

  dismiss(id: number): Observable<unknown> {
    return this.http.post(`${environment.apiUrl}/coaching/${id}/dismiss`, {});
  }

  optOut(): Observable<unknown> {
    return this.http.post(`${environment.apiUrl}/coaching/opt-out`, {});
  }

  optIn(): Observable<unknown> {
    return this.http.post(`${environment.apiUrl}/coaching/opt-in`, {});
  }
}
