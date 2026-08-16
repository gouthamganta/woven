import {
  Component, ChangeDetectionStrategy, ChangeDetectorRef,
  ElementRef, ViewChild, AfterViewChecked, Inject, PLATFORM_ID,
} from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { SupportService, SupportMessage } from '../../services/support.service';

@Component({
  selector: 'woven-assistant',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Floating orb trigger -->
    <button
      class="orb"
      [class.open]="isOpen"
      (click)="toggle()"
      aria-label="Woven assistant"
      title="Ask Woven"
    >
      <span class="orbCore"></span>
      <span class="orbRing r1"></span>
      <span class="orbRing r2"></span>
    </button>

    <!-- Chat sheet -->
    <div class="sheet" [class.visible]="isOpen" role="dialog" aria-label="Woven assistant">

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
      bottom: 88px;
      right: 18px;
      width: 52px;
      height: 52px;
      border: none;
      background: none;
      cursor: pointer;
      padding: 0;
      z-index: 200;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .orbCore {
      position: absolute;
      width: 44px;
      height: 44px;
      border-radius: 50%;
      background: conic-gradient(
        from 180deg,
        var(--gold-400) 0%,
        var(--rose-400) 50%,
        var(--plum-400) 100%
      );
      box-shadow:
        0 0 16px rgba(212,160,23,0.4),
        0 0 32px rgba(212,160,23,0.15);
      animation: orbBreath 3s ease-in-out infinite;
      transition: transform 0.2s ease, box-shadow 0.2s ease;
    }

    .orb:hover .orbCore {
      transform: scale(1.08);
      box-shadow:
        0 0 20px rgba(212,160,23,0.55),
        0 0 40px rgba(212,160,23,0.2);
    }

    .orb.open .orbCore {
      animation: none;
      transform: scale(0.88);
      opacity: 0.6;
    }

    .orbRing {
      position: absolute;
      border-radius: 50%;
      border: 1px solid rgba(212,160,23,0.35);
      animation: orbRipple 3s ease-out infinite;
      pointer-events: none;
    }
    .orbRing.r1 { width: 52px; height: 52px; animation-delay: 0s; }
    .orbRing.r2 { width: 52px; height: 52px; animation-delay: 1.2s; }

    @keyframes orbBreath {
      0%, 100% { transform: scale(1);    box-shadow: 0 0 16px rgba(212,160,23,0.4),  0 0 32px rgba(212,160,23,0.15); }
      50%       { transform: scale(1.05); box-shadow: 0 0 22px rgba(212,160,23,0.55), 0 0 44px rgba(212,160,23,0.22); }
    }

    @keyframes orbRipple {
      0%   { transform: scale(0.85); opacity: 0.7; }
      100% { transform: scale(1.6);  opacity: 0; }
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
      display: block;
      width: 32px;
      height: 32px;
      border-radius: 50%;
      background: conic-gradient(from 180deg, var(--gold-400) 0%, var(--rose-400) 50%, var(--plum-400) 100%);
      box-shadow: 0 0 10px rgba(212,160,23,0.3);
      flex-shrink: 0;
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
export class WovenAssistantComponent implements AfterViewChecked {
  @ViewChild('messageList') messageList?: ElementRef<HTMLDivElement>;
  @ViewChild('inputEl') inputEl?: ElementRef<HTMLInputElement>;

  isOpen  = false;
  draft   = '';
  thinking = false;
  history: SupportMessage[] = [];

  private shouldScroll = false;
  private isBrowser: boolean;

  constructor(
    private support: SupportService,
    private cdr: ChangeDetectorRef,
    @Inject(PLATFORM_ID) platformId: object,
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  ngAfterViewChecked() {
    if (this.shouldScroll) {
      this.scrollToBottom();
      this.shouldScroll = false;
    }
  }

  toggle() {
    this.isOpen ? this.close() : this.open();
  }

  open() {
    this.isOpen = true;
    this.cdr.markForCheck();
    if (this.isBrowser) {
      setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
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
}
