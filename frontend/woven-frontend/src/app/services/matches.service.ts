import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { Observable } from 'rxjs';

export type MatchOther = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
};

export type MatchListItem = {
  matchId: string;
  matchType: string;
  edgeOwnerId?: number | null;
  balloonState: string;
  createdAt: string;
  expiresAt: string;
  bothMessagedAt?: string | null;
  findLoveAt?: string | null;

  // ✅ provided by backend in list endpoints
  showFindLove?: boolean;
  showBalloonTimer?: boolean;
  reflectionSecondsLeft?: number;

  other?: MatchOther | null;
};

export type MatchesListResponse = {
  count: number;
  matches: MatchListItem[];
};

export type ProfileAccessResponse = {
  matchId: string;
  accessLevel: 'FULL' | 'LIMITED';
  reason: string;

  showBalloonTimer?: boolean;
  reflectionSecondsLeft?: number;
  showFindLove?: boolean;
};

export type MatchPublicPreview = {
  name: string;
  age?: number | null;
  gender?: string | null;
  location?: string | null;
  bio?: string | null;
  intent?: {
    primaryIntent?: string | null;
    openness?: string[] | null;
  } | null;
  photos: { url: string; caption?: string | null; sortOrder: number }[];
  optionalPublic: { key: string; value: string }[];
};

export type MatchProfileResponse = {
  matchId: string;
  accessLevel: 'FULL' | 'LIMITED';
  reason: string;

  showBalloonTimer?: boolean;
  reflectionSecondsLeft?: number;
  showFindLove?: boolean;

  publicPreview: MatchPublicPreview;
};

@Injectable({ providedIn: 'root' })
export class MatchesService {
  constructor(private http: HttpClient) {}

  list(): Observable<MatchesListResponse> {
    return this.http.get<MatchesListResponse>(`${environment.apiUrl}/matches`);
  }

  profileAccess(matchId: string): Observable<ProfileAccessResponse> {
    return this.http.get<ProfileAccessResponse>(
      `${environment.apiUrl}/matches/${matchId}/profile-access`
    );
  }

  // ✅ NEW: backend endpoint you added
  profile(matchId: string): Observable<MatchProfileResponse> {
    return this.http.get<MatchProfileResponse>(
      `${environment.apiUrl}/matches/${matchId}/profile`
    );
  }

  pop(matchId: string): Observable<any> {
    return this.http.post(`${environment.apiUrl}/matches/${matchId}/pop`, {});
  }

  unmatch(matchId: string, rating?: number): Observable<any> {
    const body = rating !== undefined ? { rating } : {};
    return this.http.post(`${environment.apiUrl}/matches/${matchId}/unmatch`, body);
  }

  block(matchId: string): Observable<any> {
    return this.http.post(`${environment.apiUrl}/matches/${matchId}/block`, {});
  }
}
