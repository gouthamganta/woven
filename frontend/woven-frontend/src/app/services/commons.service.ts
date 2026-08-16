import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

export interface CommonsTile {
  tileId: string;
  contentType: string;
  contentText: string | null;
  mediaUrl: string | null;
  createdAt: string;
  similarity: number;
}

export interface CommonsFeedResponse {
  page: number;
  sessionId: string;
  count: number;
  tiles: CommonsTile[];
}

@Injectable({ providedIn: 'root' })
export class CommonsService {
  constructor(private http: HttpClient) {}

  getFeed(page = 1, sessionId?: string) {
    const params: any = { page };
    if (sessionId) params['sessionId'] = sessionId;
    return this.http.get<CommonsFeedResponse>(`${environment.apiUrl}/commons`, { params });
  }

  refresh() {
    return this.http.post<{ refreshed: boolean }>(`${environment.apiUrl}/commons/refresh`, {});
  }

  recordView(tileId: string, durationMs?: number) {
    return this.http.post<{ recorded: boolean }>(
      `${environment.apiUrl}/commons/${tileId}/view`,
      durationMs != null ? { durationMs } : {}
    );
  }

  createTile(contentType: string, contentText?: string, mediaUrl?: string) {
    return this.http.post<{ tileId: string }>(`${environment.apiUrl}/tiles`, {
      contentType,
      contentText: contentText ?? null,
      mediaUrl: mediaUrl ?? null,
    });
  }

  orbitTile(tileId: string) {
    return this.http.post<{ relationshipType: string; mutualDetected: boolean }>(
      `${environment.apiUrl}/orbit/${tileId}`,
      {}
    );
  }
}
