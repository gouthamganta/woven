import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-game-message-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './game-message-card.component.html',
  styleUrls: ['./game-message-card.component.scss'],
})
export class GameMessageCardComponent {
  @Input({ required: true }) meUserId!: number;
  @Input({ required: true }) message!: {
    senderUserId: number;
    body: string;
    createdAt: string;
    messageType?: string;
    meta?: any;
  };

  @Output() accept = new EventEmitter<string>();
  @Output() reject = new EventEmitter<string>();
  @Output() openResult = new EventEmitter<string>();

  // ✅ NEW: typed play output to open inline modal
  @Output() play = new EventEmitter<{ sessionId: string; gameType: string }>();

  get meta() {
    return this.message?.meta ?? {};
  }

  get sessionId(): string | null {
    return this.meta?.sessionId ?? null;
  }

  get gameType(): string {
    return this.meta?.gameType ?? 'GAME';
  }

  get status(): string {
    return this.meta?.status ?? '';
  }

  get expiresAt(): Date | null {
    const v = this.meta?.expiresAt;
    return v ? new Date(v) : null;
  }

  get isExpired(): boolean {
    const exp = this.expiresAt;
    return exp ? Date.now() > exp.getTime() : false;
  }

  get isPending(): boolean {
    return (this.status || '').toUpperCase() === 'PENDING';
  }

  get isActive(): boolean {
    return (this.status || '').toUpperCase() === 'ACTIVE';
  }

  get isCompleted(): boolean {
    return (this.status || '').toUpperCase() === 'COMPLETED';
  }

  get title(): string {
    return this.gameType.replaceAll('_', ' ');
  }

  // ✅ Check if current user is the one who sent the game invite
  get isInitiator(): boolean {
    return this.message.senderUserId === this.meUserId;
  }

  // ✅ Accept/Reject ONLY for receiver
  get shouldShowAcceptReject(): boolean {
    return this.isPending && !this.isExpired && !this.isInitiator && !!this.sessionId;
  }

  // ✅ Waiting message for sender while pending
  get shouldShowWaiting(): boolean {
    return this.isPending && !this.isExpired && this.isInitiator;
  }

  // ✅ Play button for both users when active
  get shouldShowPlay(): boolean {
    return this.isActive && !!this.sessionId;
  }

  // ✅ View Results when completed
  get shouldShowResults(): boolean {
    return this.isCompleted && !!this.sessionId;
  }

  onAccept() {
    if (this.sessionId) {
      console.log('✅ Accepting game:', this.sessionId);
      this.accept.emit(this.sessionId);
    }
  }

  onReject() {
    if (this.sessionId) {
      console.log('❌ Rejecting game:', this.sessionId);
      this.reject.emit(this.sessionId);
    }
  }

  // ✅ FIXED: emit play event instead of alert
  onPlay() {
    if (!this.sessionId) return;

    console.log('🎮 Play clicked -> emitting to parent:', this.sessionId, this.gameType);
    this.play.emit({
      sessionId: this.sessionId,
      gameType: this.gameType,
    });
  }

  onOpenResult() {
    if (this.sessionId) {
      console.log('👁 Viewing results:', this.sessionId);
      this.openResult.emit(this.sessionId);
    }
  }
}
