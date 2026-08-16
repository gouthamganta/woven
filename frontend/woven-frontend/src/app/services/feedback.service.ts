import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export type FeedbackPrompt = {
  hasPendingPrompt: boolean;
  matchId?: string;
  partnerFirstName?: string;
  triggerType?: string;
};

export type FeedbackSubmit = {
  metInPerson: boolean;
  stars?: number | null;
  feltRightText?: string | null;
  feltOffText?: string | null;
  meetAgain?: string | null;
};

@Injectable({ providedIn: 'root' })
export class FeedbackService {
  private readonly api = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getPrompt(): Observable<FeedbackPrompt> {
    return this.http.get<FeedbackPrompt>(`${this.api}/me/feedback-prompt`, {
      headers: this.authHeaders()
    });
  }

  submit(matchId: string, body: FeedbackSubmit): Observable<{ submitted: boolean }> {
    return this.http.post<{ submitted: boolean }>(
      `${this.api}/matches/${matchId}/feedback`,
      body,
      { headers: this.authHeaders() }
    );
  }

  private authHeaders(): HttpHeaders {
    const token = localStorage.getItem('accessToken') ?? '';
    return new HttpHeaders({ Authorization: `Bearer ${token}` });
  }
}
