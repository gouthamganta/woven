import { Component, Input, Output, EventEmitter, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { GamesService, GameRoundResponse } from '../../services/games.service';

type GameOption =
  | string
  | { id: string; text: string; isCorrect?: boolean };

@Component({
  selector: 'app-inline-game-player',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="gameOverlay" (click)="onClose()">
      <div class="gameModal" (click)="$event.stopPropagation()">
        
        <!-- Elegant Header -->
        <div class="header">
          <div class="headerLeft">
            <div class="gameIcon">✨</div>
            <div>
              <div class="title">{{ gameType }}</div>
              <div class="subtitle">Round {{ round?.roundNumber || 1 }} of {{ round?.totalRounds || 3 }}</div>
            </div>
          </div>
          <button class="closeBtn" (click)="onClose()">×</button>
        </div>

        <!-- Clean Progress -->
        <div class="progressTrack" *ngIf="round">
          <div class="progressFill" [style.width.%]="(round.roundNumber / round.totalRounds) * 100"></div>
        </div>

        <!-- Loading State -->
        <div class="content" *ngIf="loading && !waitingForOther">
          <div class="centerState">
            <div class="elegantSpinner"></div>
            <div class="stateTitle">Preparing your round</div>
          </div>
        </div>

        <!-- Playing Round -->
        <div class="content scrollable" *ngIf="!loading && round && !waitingForOther">
          
          <!-- Role Card -->
          <div class="roleCard" [class.guesser]="round.isGuesser">
            <div class="roleEmoji">{{ round.isGuesser ? '🎯' : '💭' }}</div>
            <div class="roleInfo">
              <div class="roleLabel">{{ round.isGuesser ? 'Your Turn to Guess' : 'Your Turn to Answer' }}</div>
              <div class="roleDesc">{{ round.isGuesser ? 'Predict their answers' : 'Be honest about yourself' }}</div>
            </div>
          </div>

          <!-- Questions -->
          <div class="questionsContainer">
            <div class="questionBlock" *ngFor="let q of round.questions; let i = index">
              <div class="qLabel">Question {{ i + 1 }}</div>
              <div class="qText">{{ getQuestionText(q.text) }}</div>

              <div class="optionsList">
                <button
                  *ngFor="let opt of q.options"
                  class="optionItem"
                  [class.chosen]="isSelected(q.id, opt)"
                  (click)="selectAnswer(q.id, opt)"
                >
                  <div class="optionRadio">
                    <div class="radioDot" *ngIf="isSelected(q.id, opt)"></div>
                  </div>
                  <span class="optionLabel">{{ getOptionText(opt) }}</span>
                </button>
              </div>
            </div>
          </div>

          <!-- Submit -->
          <div class="submitArea">
            <button
              class="submitButton"
              [disabled]="!canSubmit()"
              [class.active]="canSubmit()"
              (click)="submitAnswers()"
            >
              {{ round.isGuesser ? 'Submit Guesses' : 'Submit Answers' }}
            </button>
            <div class="submitHint" *ngIf="!canSubmit()">
              Please answer all questions
            </div>
          </div>
        </div>

        <!-- Waiting State -->
        <div class="content" *ngIf="waitingForOther">
          <div class="centerState">
            <div class="waitingPulse">
              <div class="heartIcon">💝</div>
            </div>
            <div class="stateTitle">Waiting for them</div>
            <div class="stateDesc">They're thinking about their answers...</div>
          </div>
        </div>

        <!-- Error State -->
        <div class="content" *ngIf="error">
          <div class="centerState">
            <div class="errorIcon">⚠️</div>
            <div class="stateTitle">Something went wrong</div>
            <div class="stateDesc">{{ error }}</div>
            <button class="retryButton" (click)="loadRound()">Try Again</button>
          </div>
        </div>

      </div>
    </div>
  `,
  styles: [`
    * {
      box-sizing: border-box;
    }

    .gameOverlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.75);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
      animation: fadeIn 0.25s ease;
    }

    @keyframes fadeIn {
      from { opacity: 0; }
      to { opacity: 1; }
    }

    .gameModal {
      background: #ffffff;
      border-radius: 20px;
      max-width: 580px;
      width: 100%;
      max-height: 90vh;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.4);
      animation: slideUp 0.3s cubic-bezier(0.16, 1, 0.3, 1);
    }

    @keyframes slideUp {
      from { 
        transform: translateY(40px); 
        opacity: 0; 
      }
      to { 
        transform: translateY(0); 
        opacity: 1; 
      }
    }

    /* Header */
    .header {
      padding: 24px 28px;
      background: linear-gradient(135deg, #ff6b9d 0%, #c94b8f 100%);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .headerLeft {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .gameIcon {
      font-size: 28px;
    }

    .title {
      font-size: 20px;
      font-weight: 700;
      color: white;
      letter-spacing: -0.3px;
    }

    .subtitle {
      font-size: 13px;
      color: rgba(255, 255, 255, 0.85);
      font-weight: 500;
      margin-top: 2px;
    }

    .closeBtn {
      background: rgba(255, 255, 255, 0.25);
      border: none;
      color: white;
      width: 36px;
      height: 36px;
      border-radius: 50%;
      font-size: 26px;
      line-height: 1;
      cursor: pointer;
      transition: all 0.2s ease;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .closeBtn:hover {
      background: rgba(255, 255, 255, 0.35);
      transform: scale(1.08);
    }

    /* Progress */
    .progressTrack {
      height: 4px;
      background: #f0f0f0;
      position: relative;
    }

    .progressFill {
      height: 100%;
      background: linear-gradient(90deg, #ff6b9d, #c94b8f);
      transition: width 0.5s cubic-bezier(0.16, 1, 0.3, 1);
    }

    /* Content */
    .content {
      flex: 1;
      padding: 28px;
      overflow-y: auto;
    }

    .content.scrollable::-webkit-scrollbar {
      width: 6px;
    }

    .content.scrollable::-webkit-scrollbar-track {
      background: transparent;
    }

    .content.scrollable::-webkit-scrollbar-thumb {
      background: #e0e0e0;
      border-radius: 10px;
    }

    .content.scrollable::-webkit-scrollbar-thumb:hover {
      background: #d0d0d0;
    }

    /* Center States */
    .centerState {
      text-align: center;
      padding: 60px 20px;
    }

    .elegantSpinner {
      width: 50px;
      height: 50px;
      border: 3px solid #f0f0f0;
      border-top-color: #ff6b9d;
      border-radius: 50%;
      margin: 0 auto 24px;
      animation: spin 1s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .waitingPulse {
      width: 80px;
      height: 80px;
      margin: 0 auto 24px;
      display: flex;
      align-items: center;
      justify-content: center;
      position: relative;
    }

    .waitingPulse::before,
    .waitingPulse::after {
      content: '';
      position: absolute;
      width: 100%;
      height: 100%;
      border: 2px solid #ff6b9d;
      border-radius: 50%;
      animation: pulse 2s ease-out infinite;
    }

    .waitingPulse::after {
      animation-delay: 1s;
    }

    @keyframes pulse {
      0% { 
        transform: scale(0.8); 
        opacity: 1; 
      }
      100% { 
        transform: scale(1.3); 
        opacity: 0; 
      }
    }

    .heartIcon {
      font-size: 36px;
      position: relative;
      z-index: 1;
    }

    .errorIcon {
      font-size: 48px;
      margin-bottom: 20px;
    }

    .stateTitle {
      font-size: 20px;
      font-weight: 700;
      color: #1a1a1a;
      margin-bottom: 8px;
    }

    .stateDesc {
      font-size: 15px;
      color: #666;
      line-height: 1.5;
    }

    /* Role Card */
    .roleCard {
      background: linear-gradient(135deg, #fff5f7 0%, #ffe8ee 100%);
      border: 2px solid #ffc9d6;
      border-radius: 14px;
      padding: 18px 20px;
      display: flex;
      align-items: center;
      gap: 16px;
      margin-bottom: 28px;
    }

    .roleCard.guesser {
      background: linear-gradient(135deg, #fff8f0 0%, #ffeedd 100%);
      border-color: #ffd4a8;
    }

    .roleEmoji {
      font-size: 32px;
      flex-shrink: 0;
    }

    .roleInfo {
      flex: 1;
    }

    .roleLabel {
      font-size: 16px;
      font-weight: 700;
      color: #1a1a1a;
      margin-bottom: 3px;
    }

    .roleDesc {
      font-size: 13px;
      color: #666;
      font-weight: 500;
    }

    /* Questions */
    .questionsContainer {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .questionBlock {
      background: #fafafa;
      border-radius: 14px;
      padding: 20px;
      transition: all 0.3s ease;
    }

    .questionBlock:hover {
      background: #f5f5f5;
    }

    .qLabel {
      font-size: 12px;
      font-weight: 700;
      color: #ff6b9d;
      text-transform: uppercase;
      letter-spacing: 0.8px;
      margin-bottom: 8px;
    }

    .qText {
      font-size: 17px;
      font-weight: 600;
      color: #1a1a1a;
      margin-bottom: 16px;
      line-height: 1.4;
    }

    /* Options */
    .optionsList {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .optionItem {
      background: white;
      border: 2px solid #e8e8e8;
      border-radius: 10px;
      padding: 14px 16px;
      display: flex;
      align-items: center;
      gap: 12px;
      cursor: pointer;
      transition: all 0.2s ease;
      text-align: left;
    }

    .optionItem:hover {
      border-color: #ff6b9d;
      background: #fffcfd;
      transform: translateX(2px);
    }

    .optionItem.chosen {
      border-color: #ff6b9d;
      background: linear-gradient(135deg, #ff6b9d 0%, #c94b8f 100%);
    }

    .optionRadio {
      width: 20px;
      height: 20px;
      border: 2px solid #d0d0d0;
      border-radius: 50%;
      flex-shrink: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s ease;
    }

    .optionItem:hover .optionRadio {
      border-color: #ff6b9d;
    }

    .optionItem.chosen .optionRadio {
      border-color: white;
      background: white;
    }

    .radioDot {
      width: 10px;
      height: 10px;
      background: #ff6b9d;
      border-radius: 50%;
    }

    .optionLabel {
      font-size: 15px;
      font-weight: 600;
      color: #2a2a2a;
      flex: 1;
    }

    .optionItem.chosen .optionLabel {
      color: white;
    }

    /* Submit */
    .submitArea {
      margin-top: 32px;
      text-align: center;
    }

    .submitButton {
      width: 100%;
      padding: 16px;
      background: #d0d0d0;
      color: white;
      border: none;
      border-radius: 12px;
      font-size: 16px;
      font-weight: 700;
      cursor: not-allowed;
      transition: all 0.3s cubic-bezier(0.16, 1, 0.3, 1);
    }

    .submitButton.active {
      background: linear-gradient(135deg, #ff6b9d 0%, #c94b8f 100%);
      cursor: pointer;
      box-shadow: 0 6px 20px rgba(255, 107, 157, 0.35);
    }

    .submitButton.active:hover {
      transform: translateY(-2px);
      box-shadow: 0 8px 28px rgba(255, 107, 157, 0.45);
    }

    .submitButton.active:active {
      transform: translateY(0);
    }

    .submitHint {
      font-size: 13px;
      color: #999;
      margin-top: 10px;
      font-weight: 500;
    }

    .retryButton {
      margin-top: 20px;
      padding: 12px 32px;
      background: linear-gradient(135deg, #ff6b9d 0%, #c94b8f 100%);
      color: white;
      border: none;
      border-radius: 10px;
      font-size: 15px;
      font-weight: 700;
      cursor: pointer;
      transition: all 0.2s ease;
    }

    .retryButton:hover {
      transform: translateY(-2px);
      box-shadow: 0 6px 20px rgba(255, 107, 157, 0.35);
    }

    /* Mobile */
    @media (max-width: 600px) {
      .gameModal {
        border-radius: 16px;
      }

      .header {
        padding: 20px 22px;
      }

      .title {
        font-size: 18px;
      }

      .gameIcon {
        font-size: 24px;
      }

      .content {
        padding: 24px 20px;
      }

      .qText {
        font-size: 16px;
      }

      .optionItem {
        padding: 12px 14px;
      }

      .optionLabel {
        font-size: 14px;
      }
    }
  `]
})
export class InlineGamePlayerComponent implements OnInit, OnDestroy {
  @Input() sessionId!: string;
  @Input() gameType: string = 'GAME';
  @Output() close = new EventEmitter<void>();
  @Output() completed = new EventEmitter<void>();

  loading = true;
  error = '';
  round: GameRoundResponse | null = null;
  answers: Record<string, string> = {};
  waitingForOther = false;

  private pollInterval: any;

  constructor(private games: GamesService) {}

  ngOnInit() {
    this.loadRound();
  }

  ngOnDestroy() {
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
    }
  }

  loadRound() {
    this.loading = true;
    this.error = '';
    this.answers = {};

    this.games.getCurrentRound(this.sessionId).subscribe({
      next: (data: GameRoundResponse) => {
        this.round = data;
        this.loading = false;

        if (data.hasAnswered) {
          this.startPolling();
        }
      },
      error: (err: any) => {
        console.error('❌ Load round error:', err);
        this.error = 'Could not load round';
        this.loading = false;
      }
    });
  }

  getOptionText(opt: GameOption): string {
    return typeof opt === 'string' ? opt : opt?.text ?? '';
  }

  isSelected(questionId: string, opt: GameOption): boolean {
    return this.answers[questionId] === this.getOptionText(opt);
  }

  selectAnswer(questionId: string, opt: GameOption) {
    this.answers[questionId] = this.getOptionText(opt);
  }

  canSubmit(): boolean {
    if (!this.round) return false;
    return this.round.questions.every((q: any) => this.answers[q.id]);
  }

  getQuestionText(qText: string): string {
    if (!this.round) return qText;

    // If target is answering about themselves, flip "their" -> "your"
    if (!this.round.isGuesser) {
      return this.toSecondPerson(qText);
    }

    // If guesser is guessing, keep it third-person
    return qText;
  }

  private toSecondPerson(text: string): string {
    // Small, safe replacements that cover current phrasing.
    // Order matters (longer first).
    return (text || '')
      .replace(/\bTheir\b/g, 'Your')
      .replace(/\btheir\b/g, 'your')
      .replace(/\bThey\b/g, 'You')
      .replace(/\bthey\b/g, 'you')
      .replace(/\bThem\b/g, 'You')
      .replace(/\bthem\b/g, 'you')
      .replace(/\bAre they\b/g, 'Are you')
      .replace(/\bDo they\b/g, 'Do you')
      .replace(/\bWould they\b/g, 'Would you')
      .replace(/\bHow do they\b/g, 'How do you')
      .replace(/\bWhat do they\b/g, 'What do you');
  }

  submitAnswers() {
    if (!this.canSubmit() || !this.round) return;

    this.loading = true;

    const submitCall = this.round.isGuesser
      ? this.games.submitGuesses(this.sessionId, this.answers)
      : this.games.submitTargetAnswers(this.sessionId, this.answers);

    submitCall.subscribe({
      next: (result: any) => {
        console.log('✅ Submit result:', result);
        this.loading = false;

        if (result.status === 'WAITING_FOR_TARGET') {
          this.startPolling();
        } else if (result.status === 'NEXT_ROUND') {
          setTimeout(() => this.loadRound(), 1000);
        } else if (result.status === 'GAME_COMPLETE') {
          setTimeout(() => {
            this.completed.emit();
            this.close.emit();
          }, 1000);
        }
      },
      error: (err: any) => {
        console.error('❌ Submit error:', err);
        this.error = 'Could not submit answers';
        this.loading = false;
      }
    });
  }

  startPolling() {
    this.waitingForOther = true;

    this.pollInterval = setInterval(() => {
      this.games.getCurrentRound(this.sessionId).subscribe({
        next: (data: GameRoundResponse) => {
          if (data.roundNumber !== this.round?.roundNumber) {
            clearInterval(this.pollInterval);
            this.waitingForOther = false;
            this.loadRound();
          }
        },
        error: (err: any) => {
          console.error('❌ Poll error:', err);
          if (err.status === 404 || err.status === 400) {
            clearInterval(this.pollInterval);
            this.waitingForOther = false;
            this.completed.emit();
            this.close.emit();
          }
        }
      });
    }, 2000);
  }

  onClose() {
    this.close.emit();
  }
}