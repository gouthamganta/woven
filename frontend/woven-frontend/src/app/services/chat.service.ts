import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { Observable } from 'rxjs';

// Message types for rendering different UI
export type ChatMessageType = '' | 'SYSTEM' | 'GAME';

// Validation constants
export const CHAT_MESSAGE_MAX_LENGTH = 1000;
export const CHAT_MESSAGE_MIN_LENGTH = 1;

export type ChatOther = {
  userId: number;
  fullName: string;
  profilePhoto?: string | null;
  lastActiveAt?: string | null;
};

export type ChatLastMessage = {
  body: string;
  createdAt: string;
  senderUserId: number;
  messageType?: ChatMessageType;
  meta?: any;
};

export type ChatListItem = {
  threadId: string;
  matchId: string;
  title?: string;

  matchType: string;
  edgeOwnerId?: number | null;

  expiresAt?: string | null;
  bothMessagedAt?: string | null;
  findLoveAt?: string | null;

  showFindLove?: boolean;
  showBalloonTimer?: boolean;
  reflectionSecondsLeft?: number;

  other?: ChatOther | null;
  lastMessage?: ChatLastMessage | null;
  updatedAt?: string;

  // Trial fields
  isTrial?: boolean;
  trialEndsAt?: string | null;
  trialSecondsLeft?: number;
};

export type ChatsListResponse = {
  meUserId?: number;
  count: number;
  chats: ChatListItem[];
};

export type StartChatResponse = {
  threadId: string;
  matchId: string;
};

export type ChatMessage = {
  messageId: string;
  senderUserId: number;
  body: string;
  messageType?: ChatMessageType;
  meta?: any;
  createdAt: string;
};

export type ChatNotePin = {
  fromUserId: number;
  noteText: string;
  choice: string;
  createdAt: string;
};

export type ChatThreadResponse = {
  meUserId?: number;

  threadId: string;
  matchId: string;
  matchType?: string;

  balloonState: string;

  expiresAt?: string | null;
  bothMessagedAt?: string | null;
  findLoveAt?: string | null;

  showFindLove?: boolean;
  showBalloonTimer?: boolean;
  reflectionSecondsLeft?: number;

  dateIdea?: string | null;
  dateIdeas?: string[] | null;
  chatNotes?: ChatNotePin[];

  other?: ChatOther | null;
  messages: ChatMessage[];

  // Trial fields
  isTrial?: boolean;
  trialEndsAt?: string | null;
  trialSecondsLeft?: number;
  canMakeDecision?: boolean;
  isUserA?: boolean;
  userADecision?: string | null;
  userBDecision?: string | null;
};

export type SendMessageResponse = {
  status: 'SENT';
  messageId: string;
  createdAt: string;
};

@Injectable({ providedIn: 'root' })
export class ChatService {
  constructor(private http: HttpClient) {}

  list(): Observable<ChatsListResponse> {
    return this.http.get<ChatsListResponse>(`${environment.apiUrl}/chats`);
  }

  start(matchId: string): Observable<StartChatResponse> {
    return this.http.post<StartChatResponse>(`${environment.apiUrl}/chats/start`, { matchId });
  }

  thread(threadId: string): Observable<ChatThreadResponse> {
    return this.http.get<ChatThreadResponse>(`${environment.apiUrl}/chats/${threadId}`);
  }

  send(threadId: string, body: string): Observable<SendMessageResponse> {
    const trimmedBody = body?.trim() ?? '';
    if (trimmedBody.length < CHAT_MESSAGE_MIN_LENGTH) {
      throw new Error('Message cannot be empty');
    }
    if (trimmedBody.length > CHAT_MESSAGE_MAX_LENGTH) {
      throw new Error(`Message cannot exceed ${CHAT_MESSAGE_MAX_LENGTH} characters`);
    }
    return this.http.post<SendMessageResponse>(
      `${environment.apiUrl}/chats/${threadId}/messages`,
      { body: trimmedBody }
    );
  }

  trialDecision(threadId: string, decision: 'CONTINUE' | 'END' | 'BLOCK', endReason?: string): Observable<any> {
    const body: any = { decision };
    if (endReason) body.endReason = endReason;
    return this.http.post(`${environment.apiUrl}/chats/${threadId}/trial-decision`, body);
  }

  expressDateInterest(threadId: string, ideaIndex: number, ideaText: string): Observable<{ mutualInterest: boolean }> {
    return this.http.post<{ mutualInterest: boolean }>(
      `${environment.apiUrl}/chats/${threadId}/date-interest`,
      { ideaIndex, ideaText }
    );
  }

  sendVoiceMessage(threadId: string, audioUrl: string, durationSecs: number): Observable<{ messageId: string; createdAt: string }> {
    return this.http.post<{ messageId: string; createdAt: string }>(
      `${environment.apiUrl}/chats/${threadId}/voice-message`,
      { audioUrl, durationSecs }
    );
  }

  voiceListened(threadId: string, messageId: string): Observable<any> {
    return this.http.post(
      `${environment.apiUrl}/chats/${threadId}/messages/${messageId}/voice-listened`,
      {}
    );
  }
}