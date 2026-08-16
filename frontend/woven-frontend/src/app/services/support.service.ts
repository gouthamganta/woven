import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

export type SupportMessage = { role: 'user' | 'assistant'; content: string };

@Injectable({ providedIn: 'root' })
export class SupportService {
  constructor(private http: HttpClient) {}

  chat(messages: SupportMessage[]) {
    return this.http.post<{ reply: string }>(
      `${environment.apiUrl}/support/chat`,
      { messages }
    );
  }
}
