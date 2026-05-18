import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../environments/environment';

export interface DeckReadyEvent        { date: string }
export interface MomentReceivedEvent   { matchId: string; fromUserId: number }
export interface MomentExpiredEvent    { matchId: string }
export interface GameInviteEvent       { sessionId: string; matchId: string; gameType: string; expiresAt: string }
export interface GameStartedEvent      { sessionId: string; matchId: string; gameType: string }
export interface GameCompletedEvent    { sessionId: string; matchId: string; gameType: string; winnerUserId: number | null }

@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private connection: signalR.HubConnection | null = null;

  // Subjects — subscribe in components; each fires when the matching server event arrives.
  readonly deckReady$          = new Subject<DeckReadyEvent>();
  readonly momentReceived$     = new Subject<MomentReceivedEvent>();
  readonly momentExpired$      = new Subject<MomentExpiredEvent>();
  readonly gameInviteReceived$ = new Subject<GameInviteEvent>();
  readonly gameStarted$        = new Subject<GameStartedEvent>();
  readonly gameCompleted$      = new Subject<GameCompletedEvent>();

  /**
   * Open the SignalR connection. Call this once after the user has logged in
   * (token is already in localStorage). Safe to call multiple times — idempotent.
   */
  start(): void {
    if (!this.isBrowser || this.connection) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hubs/woven`, {
        // Read the token fresh on every (re)connect attempt so expiry is handled correctly.
        accessTokenFactory: () => localStorage.getItem('accessToken') ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.connection.on('DeckReady',          (e: DeckReadyEvent)      => this.deckReady$.next(e));
    this.connection.on('MomentReceived',     (e: MomentReceivedEvent) => this.momentReceived$.next(e));
    this.connection.on('MomentExpired',      (e: MomentExpiredEvent)  => this.momentExpired$.next(e));
    this.connection.on('GameInviteReceived', (e: GameInviteEvent)     => this.gameInviteReceived$.next(e));
    this.connection.on('GameStarted',        (e: GameStartedEvent)    => this.gameStarted$.next(e));
    this.connection.on('GameCompleted',      (e: GameCompletedEvent)  => this.gameCompleted$.next(e));

    this.connection.start().catch((err) => {
      console.warn('[Realtime] Initial connection failed:', err);
    });
  }

  /** Close the connection. Call on logout. */
  stop(): void {
    if (!this.isBrowser || !this.connection) return;
    this.connection.stop().catch(() => {});
    this.connection = null;
  }
}
