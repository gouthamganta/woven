import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { Observable } from 'rxjs';

export type MomentChoice = 'YES' | 'NO' | 'PENDING';

export type MomentsTheme = {
  id: string;
  question?: string;
  left: { label: string; emoji: string; choice: MomentChoice };
  mid: { label: string; emoji: string; choice: MomentChoice };
  right: { label: string; emoji: string; choice: MomentChoice };
};

export type LocationDto = { city?: string | null; state?: string | null };

/** ✅ NEW: Match explanation returned from backend */
export type MatchReason = {
  headline?: string | null;
  bullets?: string[] | null;
  tone?: string | null;
};

export type MomentsCard = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
  age?: number | null;
  gender?: string | null;
  location?: LocationDto | null;

  // Orchestrator fields
  score?: number | null;
  bucket?: string | null;
  reason?: MatchReason | null;

  // Rating (shown when count >= 5)
  rating?: {
    average: number;
    count: number;
    show: boolean;
  } | null;
};

export type MomentsBudget = {
  totalCap: number;
  totalUsed: number;
  totalRemaining: number;
  pendingCap: number;
  pendingUsed: number;
  pendingRemaining: number;
};

export type MomentsResponse = {
  dateUtc: string;
  theme: MomentsTheme;
  budget: MomentsBudget;
  count: number;
  cards: MomentsCard[];
};

export type RespondRequest = {
  targetUserId: number;
  choice: MomentChoice;
  source?: 'PENDING' | 'MOMENTS' | null;
};

export type RespondResult = {
  status: string;
  matchId?: string;
  edgeOwnerId?: number | null;
  reason?: string | null;
  totalUsed?: number;
  pendingUsed?: number;
  error?: string;
};

// ✅ Pending/Hold
export type PendingCard = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
  age?: number | null;
  location?: LocationDto | null;
  savedAt?: string; // ISO string
};

export type PendingResponse = {
  count: number;
  cards: PendingCard[];
};

@Injectable({ providedIn: 'root' })
export class MomentsService {
  constructor(private http: HttpClient) {}

  getMoments(): Observable<MomentsResponse> {
    return this.http.get<MomentsResponse>(`${environment.apiUrl}/moments`);
  }

  getPending(): Observable<PendingResponse> {
    return this.http.get<PendingResponse>(`${environment.apiUrl}/moments/pending`);
  }

  respond(req: RespondRequest): Observable<RespondResult> {
    return this.http.post<RespondResult>(`${environment.apiUrl}/moments/respond`, req);
  }
}
