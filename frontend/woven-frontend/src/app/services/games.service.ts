import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type GameType = 'KNOW_ME' | 'RED_GREEN_FLAG';

export interface GameAvailabilityResponse {
  available: boolean;
  gamesRemaining: number;
  reason: string | null;
  games: Array<{
    type: GameType;
    name: string;
    description: string;
    duration: string;
    icon: string;
  }>;
}

export interface GameSessionDto {
  sessionId: string;
  matchId: string;
  gameType: string;
  status: string;
  expiresAt: string;
}

export interface GameRoundDto {
  roundNumber: number;
  totalRounds: number;
  questions: Array<{
    id: string;
    text: string;
    options: Array<{ id: string; text: string; isCorrect: boolean }>;
    difficulty: string;
    category: string;
  }>;
  timeLimit: number;
  isGuesser: boolean;
  hasAnswered: boolean;
  waitingForOther: boolean;
}

export interface RoundResultDto {
  roundNumber: number;
  score?: number;
  totalQuestions: number;
  status: string;
  message: string;
}

export interface FinalResultResponse {
  sessionId: string;
  gameType: string;
  yourScore: number;
  theirScore: number;
  youWon: boolean;
  isTie: boolean;
  aiInsight: string;
  userAName?: string;
  userBName?: string;
}

// ✅ Export these types for the inline game player
export interface GameRoundResponse extends GameRoundDto {}

export interface Question {
  id: string;
  text: string;
  options: string[];
}

export interface RoundResultResponse extends RoundResultDto {}

@Injectable({ providedIn: 'root' })
export class GamesService {
  // ✅ Use empty baseUrl - requests will go through the proxy
  private readonly baseUrl = '';

  constructor(private http: HttpClient) {}

  getAvailability(matchId: string): Observable<GameAvailabilityResponse> {
    return this.http.get<GameAvailabilityResponse>(`${this.baseUrl}games/matches/${matchId}/availability`);
  }

  createSession(matchId: string, gameType: GameType): Observable<GameSessionDto> {
    return this.http.post<GameSessionDto>(`${this.baseUrl}games/matches/${matchId}/sessions`, { gameType });
  }

  acceptSession(sessionId: string): Observable<{ status: string; message: string }> {
    return this.http.post<{ status: string; message: string }>(`${this.baseUrl}games/sessions/${sessionId}/accept`, {});
  }

  rejectSession(sessionId: string): Observable<{ status: string }> {
    return this.http.post<{ status: string }>(`${this.baseUrl}games/sessions/${sessionId}/reject`, {});
  }

  getRound(sessionId: string): Observable<GameRoundDto> {
    return this.http.get<GameRoundDto>(`${this.baseUrl}games/sessions/${sessionId}/round`);
  }

  // ✅ Add alias for getCurrentRound (used by inline game player)
  getCurrentRound(sessionId: string): Observable<GameRoundResponse> {
    return this.getRound(sessionId);
  }

  submitAnswers(sessionId: string, answers: Record<string, string>): Observable<RoundResultDto> {
    return this.http.post<RoundResultDto>(`${this.baseUrl}games/sessions/${sessionId}/answers`, { answers });
  }

  // ✅ Add alias for submitGuesses (used by inline game player)
  submitGuesses(sessionId: string, answers: Record<string, string>): Observable<RoundResultResponse> {
    return this.submitAnswers(sessionId, answers);
  }

  submitTargetAnswers(sessionId: string, answers: Record<string, string>): Observable<RoundResultDto> {
    return this.http.post<RoundResultDto>(`${this.baseUrl}games/sessions/${sessionId}/target-answers`, { answers });
  }

  getResult(sessionId: string): Observable<FinalResultResponse> {
    return this.http.get<FinalResultResponse>(`${this.baseUrl}games/sessions/${sessionId}/result`);
  }
}