import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { Observable } from 'rxjs';

export type MomentAction = 'MAGICAL' | 'LOGICAL' | 'PASS';
export type MomentSource = 'TODAY' | 'LIKED_YOU' | null;

export type LocationDto = { city?: string | null; state?: string | null };

export type MatchReason = {
  headline?: string | null;
  bullets?: string[] | null;
  tone?: string | null;
  bridgeQuestion?: string | null;
};

export type HighlightedTile = {
  id: string;
  contentType: string;
  contentText?: string | null;
  mediaUrl?: string | null;
  isActive?: boolean;  // true = live in Commons right now, false = pinned highlight
};

export type MomentsCard = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
  gender?: string | null;
  displayPronouns?: string | null;
  location?: LocationDto | null;
  isVerified?: boolean | null;
  score?: number | null;
  bucket?: string | null;
  alreadyChoseYou?: boolean;
  reason?: MatchReason | null;
  rating?: { average: number; count: number; show: boolean } | null;
  photos?: string[] | null;
  highlightedTiles?: HighlightedTile[] | null;
  // Cinematic intro (null until Build N+1 populates)
  kenBurnsPhotoUrls?: string[] | null;
  curatedQuote?: string | null;
  narrationUrl?: string | null;
  narrationExposed?: boolean;
};

export type LikedYouCard = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
  location?: LocationDto | null;
  isVerified?: boolean | null;
  likedAt: string;
  expiresInHours: number;
  photos?: string[] | null;
  highlightedTiles?: HighlightedTile[] | null;
  rating?: { average: number; count: number; show: boolean } | null;
};

export type MomentsBudget = {
  totalCap: number;
  totalUsed: number;
  totalRemaining: number;
};

export type MomentsResponse = {
  dateUtc: string;
  budget: MomentsBudget;
  sparkBalance?: number;
  moodLine?: string | null;
  count: number;
  cards: MomentsCard[];
};

export type LikedYouResponse = {
  count: number;
  cards: LikedYouCard[];
};

export type RespondRequest = {
  targetUserId: number;
  choice: MomentAction;
  source?: MomentSource;
  timeOnCardMs?: number | null;
};

export type RespondResult = {
  status: string;
  matchId?: string;
  matchType?: string;
  edgeOwnerId?: number | null;
  reason?: string | null;
  totalUsed?: number;
  error?: string;
};

export type ChooseRequest = {
  targetUserId: number;
  choice: 'MAGICAL' | 'LOGICAL';
  noteText: string;
  source?: MomentSource;
  timeOnCardMs?: number | null;
};

export type ChooseResult = {
  status: string;
  matchId?: string;
  matchType?: string;
  edgeOwnerId?: number | null;
  reason?: string | null;
  error?: string;
  sparkBalance?: number;
};

@Injectable({ providedIn: 'root' })
export class MomentsService {
  constructor(private http: HttpClient) {}

  getMoments(): Observable<MomentsResponse> {
    return this.http.get<MomentsResponse>(`${environment.apiUrl}/moments`);
  }

  getLikedYou(): Observable<LikedYouResponse> {
    return this.http.get<LikedYouResponse>(`${environment.apiUrl}/moments/liked-you`);
  }

  respond(req: RespondRequest): Observable<RespondResult> {
    return this.http.post<RespondResult>(`${environment.apiUrl}/moments/respond`, req);
  }

  choose(req: ChooseRequest): Observable<ChooseResult> {
    return this.http.post<ChooseResult>(`${environment.apiUrl}/moments/choose`, req);
  }
}
