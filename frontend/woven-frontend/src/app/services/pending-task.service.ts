import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export type PendingTaskType = 'date_feedback' | 'weekly_coaching' | 'daily_pulse';

export interface PendingTask {
  type: PendingTaskType;
  priority: number;
  data: {
    matchId?: string;
    partnerName?: string;
    summaryId?: number;
    summaryText?: string;
    cycleId?: string;
  };
}

export interface PendingTasksResponse {
  tasks: PendingTask[];
}

@Injectable({ providedIn: 'root' })
export class PendingTaskService {
  private api = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getPendingTasks(): Observable<PendingTasksResponse> {
    return this.http.get<PendingTasksResponse>(`${this.api}/me/pending-tasks`);
  }

  logInteraction(eventType: string, context?: Record<string, any>): Observable<{logged: boolean}> {
    return this.http.post<{logged: boolean}>(`${this.api}/me/interaction-log`, {
      eventType,
      context
    });
  }
}
