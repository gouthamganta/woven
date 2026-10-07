import {
  Component, ChangeDetectionStrategy, ChangeDetectorRef,
  ElementRef, ViewChild, AfterViewChecked, OnInit, Inject, PLATFORM_ID,
} from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { SupportService, SupportMessage } from '../../services/support.service';
import { PendingTaskService, PendingTask } from '../../services/pending-task.service';
import { PulseSheetComponent } from '../../pages/home/pulse-sheet.component';
import { CoachingCardComponent } from '../coaching-card/coaching-card.component';

@Component({
  selector: 'woven-assistant',
  standalone: true,
  imports: [CommonModule, FormsModule, PulseSheetComponent, CoachingCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Floating orb trigger -->
    <button
      #orbBtn
      class="orb"
      [class.open]="isOpen"
      [class.dragging]="isDragging"
      [class.glow-red]="glowState === 'red'"
      [class.glow-white]="glowState === 'white'"
      [class.glow-gold]="glowState === 'gold'"
      [style.left.px]="orbX"
      [style.bottom.px]="orbY"
      (click)="handleClick($event)"
      (mousedown)="startDrag($event)"
      (touchstart)="startDrag($event)"
      aria-label="Woven assistant"
      [title]="badgeCount > 0 ? badgeCount + ' pending' : 'Ask Woven'"
    >
      <span class="orbCore">
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"></path>
        </svg>
      </span>
      <span class="orbBadge" *ngIf="badgeCount > 0">{{ badgeCount }}</span>
    </button>

    <!-- Pending Task Modals -->
    <app-pulse-sheet
      *ngIf="showingPulse"
      [state]="pulseState"
      (saved)="onPulseSaved($event)"
      (skipped)="onPulseSkipped()"
      (closed)="onPulseClosed()"
    ></app-pulse-sheet>

    <woven-coaching-card
      *ngIf="showingCoaching"
      [summary]="coachingSummary"
      (dismissed)="onCoachingDismissed()"
      (skipped)="onCoachingSkipped()"
    ></woven-coaching-card>

    <!-- Chat sheet -->
    <div class="sheet" [class.visible]="isOpen && !showingPulse && !showingCoaching" role="dialog" aria-label="Woven assistant">

      <!-- Handle + header -->
      <div class="sheetHead">
        <div class="handle" (click)="close()"></div>
        <div class="headRow">
          <div class="headLeft">
            <span class="headOrb"></span>
            <div>
              <div class="headName">Woven</div>
              <div class="headSub">here to help</div>
            </div>
          </div>
          <button class="closeBtn" (click)="close()">✕</button>
        </div>
      </div>

      <!-- Messages -->
      <div class="messages" #messageList>
        <!-- Welcome -->
        <div class="msg assistant" *ngIf="history.length === 0">
          <div class="bubble">
            Hey — I'm Woven. Ask me anything about the app, how things work, or just tell me what's on your mind.
          </div>
        </div>

        <ng-container *ngFor="let m of history">
          <div class="msg" [class.user]="m.role === 'user'" [class.assistant]="m.role === 'assistant'">
            <div class="bubble">{{ m.content }}</div>
          </div>
        </ng-container>

        <!-- Typing indicator -->
        <div class="msg assistant" *ngIf="thinking">
          <div class="bubble typing">
            <span></span><span></span><span></span>
          </div>
        </div>
      </div>

      <!-- Input -->
      <div class="inputRow">
        <input
          #inputEl
          class="input"
          type="text"
          [(ngModel)]="draft"
          placeholder="Ask anything…"
          maxlength="500"
          (keydown.enter)="send()"
          [disabled]="thinking"
        />
        <button class="sendBtn" (click)="send()" [disabled]="!draft.trim() || thinking">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            <path d="M14 8L2 2l2.5 6L2 14l12-6z" fill="currentColor"/>
          </svg>
        </button>
      </div>

    </div>

    <!-- Backdrop -->
    <div class="backdrop" [class.visible]="isOpen" (click)="close()"></div>
  `,
  styles: [`
    /* ── Orb trigger ──────────────────────────────────── */
    .orb {
      position: fixed;
      width: 56px;
      height: 56px;
      border: none;
      background: none;
      cursor: grab;
      padding: 0;
      z-index: 200;
      display: flex;
      align-items: center;
      justify-content: center;
      user-select: none;
      -webkit-user-select: none;
      touch-action: none;
      transition: transform 0.2s ease;
    }

    .orb.dragging {
      cursor: grabbing;
      transform: scale(1.1);
      transition: none;
    }

    .orbCore {
      width: 56px;
      height: 56px;
      border-radius: 50%;
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      box-shadow:
        0 4px 20px rgba(212,160,23,0.35),
        0 0 16px rgba(212,160,23,0.25);
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--bg-base);
      animation: orbBreath 3s ease-in-out infinite;
      transition: transform 0.2s ease, box-shadow 0.2s ease;
      border: 2px solid rgba(255,255,255,0.2);
    }

    .orb:hover .orbCore {
      transform: scale(1.05);
      box-shadow:
        0 6px 24px rgba(212,160,23,0.45),
        0 0 20px rgba(212,160,23,0.35);
    }

    .orb.open .orbCore {
      animation: none;
      transform: scale(0.9);
      opacity: 0.85;
    }

    .orb.dragging .orbCore {
      animation: none;
    }

    @keyframes orbBreath {
      0%, 100% { box-shadow: 0 4px 20px rgba(212,160,23,0.35), 0 0 16px rgba(212,160,23,0.25); }
      50%       { box-shadow: 0 6px 28px rgba(212,160,23,0.45), 0 0 22px rgba(212,160,23,0.35); }
    }

    /* Glow states */
    .orb.glow-red .orbCore {
      box-shadow: 0 0 24px #E74C3C, 0 4px 20px rgba(231,76,60,0.5);
      animation: pulse-red 2s ease-in-out infinite;
    }
    .orb.glow-white .orbCore {
      box-shadow: 0 0 24px #E8E4F3, 0 4px 20px rgba(232,228,243,0.4);
      animation: pulse-white 2s ease-in-out infinite;
    }
    .orb.glow-gold .orbCore {
      box-shadow: 0 0 28px var(--gold-400), 0 4px 20px rgba(212,160,23,0.6);
      animation: pulse-gold 2s ease-in-out infinite;
    }

    @keyframes pulse-red {
      0%, 100% { box-shadow: 0 0 24px #E74C3C, 0 4px 20px rgba(231,76,60,0.5); }
      50% { box-shadow: 0 0 32px #E74C3C, 0 6px 28px rgba(231,76,60,0.7); }
    }
    @keyframes pulse-white {
      0%, 100% { box-shadow: 0 0 24px #E8E4F3, 0 4px 20px rgba(232,228,243,0.4); }
      50% { box-shadow: 0 0 32px #E8E4F3, 0 6px 28px rgba(232,228,243,0.6); }
    }
    @keyframes pulse-gold {
      0%, 100% { box-shadow: 0 0 28px var(--gold-400), 0 4px 20px rgba(212,160,23,0.6); }
      50% { box-shadow: 0 0 36px var(--gold-400), 0 6px 28px rgba(212,160,23,0.8); }
    }

    /* Badge */
    .orbBadge {
      position: absolute;
      top: -4px;
      right: -4px;
      min-width: 20px;
      height: 20px;
      padding: 0 6px;
      background: #E74C3C;
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 11px;
      font-weight: 700;
      color: white;
      border: 2px solid var(--bg-base);
    }

    /* ── Backdrop ─────────────────────────────────────── */
    .backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0,0,0,0.4);
      z-index: 210;
      opacity: 0;
      pointer-events: none;
      transition: opacity 0.25s ease;
    }
    .backdrop.visible {
      opacity: 1;
      pointer-events: all;
    }

    /* ── Sheet ────────────────────────────────────────── */
    .sheet {
      position: fixed;
      bottom: 0;
      left: 0;
      right: 0;
      max-width: 480px;
      margin: 0 auto;
      height: 72dvh;
      background: var(--bg-surface);
      border-top: 1px solid var(--border-soft);
      border-radius: 24px 24px 0 0;
      z-index: 220;
      display: flex;
      flex-direction: column;
      transform: translateY(100%);
      transition: transform 0.3s cubic-bezier(0.32, 0.72, 0, 1);
      box-shadow: 0 -8px 40px rgba(0,0,0,0.4);
      overflow: hidden;
    }

    .sheet.visible {
      transform: translateY(0);
    }

    /* ── Sheet header ─────────────────────────────────── */
    .sheetHead {
      flex-shrink: 0;
      padding: 10px 16px 14px;
      border-bottom: 1px solid var(--border-subtle);
    }

    .handle {
      width: 36px;
      height: 4px;
      border-radius: 9999px;
      background: var(--border-soft);
      margin: 0 auto 12px;
      cursor: pointer;
    }

    .headRow {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .headLeft {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .headOrb {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 32px;
      border-radius: 50%;
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      box-shadow: 0 2px 8px rgba(212,160,23,0.3);
      flex-shrink: 0;
      color: var(--bg-base);
      font-size: 14px;
    }
    .headOrb::after {
      content: '◈';
    }

    .headName {
      font-family: var(--font-display);
      font-size: 16px;
      font-weight: 400;
      color: var(--text-primary);
      letter-spacing: -0.01em;
    }

    .headSub {
      font-family: var(--font-ui);
      font-size: 11px;
      color: var(--text-dim);
      margin-top: 1px;
    }

    .closeBtn {
      background: var(--bg-elevated);
      border: 1px solid var(--border-subtle);
      color: var(--text-dim);
      width: 30px;
      height: 30px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      font-size: 11px;
      transition: all 0.15s ease;
    }
    .closeBtn:hover { color: var(--text-muted); border-color: var(--border-soft); }

    /* ── Messages ─────────────────────────────────────── */
    .messages {
      flex: 1;
      overflow-y: auto;
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 10px;
      scrollbar-width: none;
    }
    .messages::-webkit-scrollbar { display: none; }

    .msg {
      display: flex;
      max-width: 82%;
    }
    .msg.user     { align-self: flex-end; }
    .msg.assistant { align-self: flex-start; }

    .bubble {
      padding: 11px 14px;
      border-radius: 18px;
      font-family: var(--font-ui);
      font-size: 14px;
      line-height: 1.5;
    }

    .msg.user .bubble {
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base);
      border-bottom-right-radius: 4px;
    }

    .msg.assistant .bubble {
      background: var(--bg-elevated);
      border: 1px solid var(--border-subtle);
      color: var(--text-primary);
      border-bottom-left-radius: 4px;
    }

    /* Typing indicator */
    .bubble.typing {
      display: flex;
      align-items: center;
      gap: 5px;
      padding: 14px 16px;
    }

    .bubble.typing span {
      display: block;
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--text-dim);
      animation: typingDot 1.2s ease-in-out infinite;
    }
    .bubble.typing span:nth-child(2) { animation-delay: 0.2s; }
    .bubble.typing span:nth-child(3) { animation-delay: 0.4s; }

    @keyframes typingDot {
      0%, 60%, 100% { transform: translateY(0); opacity: 0.4; }
      30%            { transform: translateY(-5px); opacity: 1; }
    }

    /* ── Input row ────────────────────────────────────── */
    .inputRow {
      flex-shrink: 0;
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 12px 20px;
      border-top: 1px solid var(--border-subtle);
    }

    .input {
      flex: 1;
      padding: 11px 14px;
      background: var(--bg-elevated);
      border: 1px solid var(--border-subtle);
      border-radius: 9999px;
      color: var(--text-primary);
      font-family: var(--font-ui);
      font-size: 14px;
      outline: none;
      transition: border-color 0.2s ease;
    }
    .input::placeholder { color: var(--text-dim); }
    .input:focus { border-color: var(--gold-400); }
    .input:disabled { opacity: 0.5; }

    .sendBtn {
      width: 40px;
      height: 40px;
      border-radius: 50%;
      border: none;
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base);
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      flex-shrink: 0;
      transition: opacity 0.15s ease, transform 0.1s ease;
      box-shadow: 0 2px 12px rgba(212,160,23,0.35);
    }
    .sendBtn:hover:not(:disabled) { opacity: 0.88; }
    .sendBtn:active:not(:disabled) { transform: scale(0.94); }
    .sendBtn:disabled { opacity: 0.35; cursor: not-allowed; }
  `],
})
export class WovenAssistantComponent implements OnInit, AfterViewChecked {
  @ViewChild('messageList') messageList?: ElementRef<HTMLDivElement>;
  @ViewChild('inputEl') inputEl?: ElementRef<HTMLInputElement>;
  @ViewChild('orbBtn') orbBtn?: ElementRef<HTMLButtonElement>;

  isOpen  = false;
  draft   = '';
  thinking = false;
  history: SupportMessage[] = [];

  // Pending tasks
  pendingTasks: PendingTask[] = [];
  badgeCount = 0;
  glowState: 'none' | 'red' | 'white' | 'gold' = 'none';

  // Modal states
  showingPulse = false;
  showingCoaching = false;
  pulseState: any = null;
  coachingSummary: any = null;

  // Drag state
  isDragging = false;
  orbX = 18; // Initial right position
  orbY = 88; // Initial bottom position
  private startX = 0;
  private startY = 0;
  private dragMoved = false;

  private shouldScroll = false;
  private isBrowser: boolean;

  constructor(
    private support: SupportService,
    private pendingTask: PendingTaskService,
    private cdr: ChangeDetectorRef,
    @Inject(PLATFORM_ID) platformId: object,
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  async ngOnInit() {
    if (this.isBrowser) {
      await this.checkPendingTasks();
    }
  }

  ngAfterViewChecked() {
    if (this.shouldScroll) {
      this.scrollToBottom();
      this.shouldScroll = false;
    }
  }

  startDrag(event: MouseEvent | TouchEvent) {
    if (!this.isBrowser) return;
    event.preventDefault();

    this.isDragging = true;
    this.dragMoved = false;

    const clientX = 'touches' in event ? event.touches[0].clientX : event.clientX;
    const clientY = 'touches' in event ? event.touches[0].clientY : event.clientY;

    this.startX = clientX - this.orbX;
    this.startY = window.innerHeight - clientY - this.orbY;

    const onMove = (e: MouseEvent | TouchEvent) => this.onDrag(e);
    const onEnd = () => this.endDrag();

    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onEnd);
    document.addEventListener('touchmove', onMove);
    document.addEventListener('touchend', onEnd);

    this.cdr.markForCheck();
  }

  onDrag(event: MouseEvent | TouchEvent) {
    if (!this.isDragging || !this.isBrowser) return;

    this.dragMoved = true;

    const clientX = 'touches' in event ? event.touches[0].clientX : event.clientX;
    const clientY = 'touches' in event ? event.touches[0].clientY : event.clientY;

    this.orbX = Math.max(0, Math.min(window.innerWidth - 56, clientX - this.startX));
    this.orbY = Math.max(0, Math.min(window.innerHeight - 56, window.innerHeight - clientY - this.startY));

    this.cdr.markForCheck();
  }

  endDrag() {
    if (!this.isDragging) return;

    this.isDragging = false;

    document.removeEventListener('mousemove', this.onDrag.bind(this));
    document.removeEventListener('mouseup', this.endDrag.bind(this));
    document.removeEventListener('touchmove', this.onDrag.bind(this));
    document.removeEventListener('touchend', this.endDrag.bind(this));

    this.cdr.markForCheck();
  }

  handleClick(event: MouseEvent) {
    if (this.dragMoved) {
      event.stopPropagation();
      event.preventDefault();
      return;
    }
    this.toggle();
  }

  async toggle() {
    if (this.isOpen) {
      this.close();
    } else {
      await this.open();
    }
  }

  async open() {
    this.isOpen = true;

    // Check if there are pending tasks to show first
    if (this.pendingTasks.length > 0) {
      const highest = this.pendingTasks[0];
      await this.showPendingTask(highest);
    }

    this.cdr.markForCheck();
    if (this.isBrowser && !this.showingPulse && !this.showingCoaching) {
      setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
      // Log assistant opened
      this.pendingTask.logInteraction('AssistantChatOpened', {
        hasPendingTasks: this.pendingTasks.length > 0
      }).subscribe();
    }
  }

  async showPendingTask(task: PendingTask) {
    // Implementation will fetch the actual data and show the modal
    if (task.type === 'daily_pulse') {
      this.showingPulse = true;
      // Pulse state would be fetched here - simplified for now
      this.pulseState = { cycleId: task.data.cycleId };
    } else if (task.type === 'weekly_coaching') {
      this.showingCoaching = true;
      this.coachingSummary = {
        id: task.data.summaryId,
        summaryText: task.data.summaryText
      };
    }
  }

  close() {
    this.isOpen = false;
    this.cdr.markForCheck();
  }

  async send() {
    const text = this.draft.trim();
    if (!text || this.thinking) return;

    this.history = [...this.history, { role: 'user', content: text }];
    this.draft = '';
    this.thinking = true;
    this.shouldScroll = true;
    this.cdr.markForCheck();

    try {
      const res = await firstValueFrom(this.support.chat(this.history));
      this.history = [...this.history, { role: 'assistant', content: res.reply }];
    } catch {
      this.history = [...this.history, {
        role: 'assistant',
        content: 'Something went wrong on my end — try again in a moment.',
      }];
    } finally {
      this.thinking = false;
      this.shouldScroll = true;
      this.cdr.markForCheck();
    }
  }

  private scrollToBottom() {
    const el = this.messageList?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }

  async checkPendingTasks() {
    try {
      const res = await firstValueFrom(this.pendingTask.getPendingTasks());
      this.pendingTasks = res.tasks;
      this.badgeCount = this.pendingTasks.length;
      this.updateGlowState();
      this.cdr.markForCheck();
    } catch {
      // Silent fail - just no badge/glow
    }
  }

  private updateGlowState() {
    if (this.pendingTasks.length === 0) {
      this.glowState = 'none';
      return;
    }

    const highest = this.pendingTasks[0];
    switch (highest.type) {
      case 'date_feedback':
        this.glowState = 'red';
        break;
      case 'weekly_coaching':
        this.glowState = 'gold';
        break;
      case 'daily_pulse':
        this.glowState = 'white';
        break;
      default:
        this.glowState = 'none';
    }
  }

  // Pulse event handlers
  async onPulseSaved(answers: any) {
    this.showingPulse = false;
    await this.pendingTask.logInteraction('DailyPulseCompleted', {
      answers
    }).toPromise();
    await this.advanceToNextPendingOrChat();
  }

  async onPulseSkipped() {
    this.showingPulse = false;
    await this.pendingTask.logInteraction('DailyPulseSkipped', {
      nextAction: this.pendingTasks.length > 1 ? 'opened_next_pending' : 'opened_assistant'
    }).toPromise();
    await this.advanceToNextPendingOrChat();
  }

  onPulseClosed() {
    this.showingPulse = false;
    this.close();
  }

  // Coaching event handlers
  async onCoachingDismissed() {
    this.showingCoaching = false;
    await this.pendingTask.logInteraction('WeeklyCoachingDismissed', {
      summaryId: this.coachingSummary?.id
    }).toPromise();
    await this.advanceToNextPendingOrChat();
  }

  async onCoachingSkipped() {
    this.showingCoaching = false;
    await this.pendingTask.logInteraction('WeeklyCoachingSkipped', {
      summaryId: this.coachingSummary?.id,
      nextAction: this.pendingTasks.length > 1 ? 'opened_next_pending' : 'opened_assistant'
    }).toPromise();
    await this.advanceToNextPendingOrChat();
  }

  // Auto-advance to next pending task or chat
  async advanceToNextPendingOrChat() {
    // Remove the completed/skipped task
    this.pendingTasks.shift();
    this.badgeCount = this.pendingTasks.length;
    this.updateGlowState();

    if (this.pendingTasks.length > 0) {
      // Show next pending task
      const next = this.pendingTasks[0];
      await this.showPendingTask(next);
    } else {
      // No more pending - show chat
      if (this.isBrowser) {
        setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
      }
    }
    this.cdr.markForCheck();
  }
}
