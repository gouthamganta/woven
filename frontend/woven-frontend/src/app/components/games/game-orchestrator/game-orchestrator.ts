import { ChangeDetectionStrategy, Component, signal, computed, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { SliderGame, SliderGameQuestion, SliderGameAnswer } from '../slider-game/slider-game';
import { RankingGame, RankingGameQuestion, RankingGameAnswer } from '../ranking-game/ranking-game';
import { SwipeGame, SwipeGameQuestion, SwipeGameAnswer } from '../swipe-game/swipe-game';
import { SelectionGame, SelectionGameQuestion, SelectionGameAnswer } from '../selection-game/selection-game';

type GameType = 'slider' | 'ranking' | 'swipe' | 'selection';

interface GameInstance {
  id: string;
  type: GameType;
  pillar: string; // Which of the 8 pillars this game maps to
  question: SliderGameQuestion | RankingGameQuestion | SwipeGameQuestion | SelectionGameQuestion;
}

interface GameAnswer {
  gameId: string;
  pillar: string;
  answer: SliderGameAnswer | RankingGameAnswer | SwipeGameAnswer | SelectionGameAnswer;
}

@Component({
  selector: 'app-game-orchestrator',
  imports: [CommonModule, SliderGame, RankingGame, SwipeGame, SelectionGame],
  templateUrl: './game-orchestrator.html',
  styleUrl: './game-orchestrator.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameOrchestrator implements OnInit {
  private cdr = inject(ChangeDetectorRef);
  private http = inject(HttpClient);
  private router = inject(Router);

  games = signal<GameInstance[]>([]);
  currentGameIndex = signal<number>(0);
  answers = signal<GameAnswer[]>([]);
  isComplete = signal<boolean>(false);
  isLoading = signal<boolean>(true);
  loadError = signal<string | null>(null);

  currentGame = computed(() => this.games()[this.currentGameIndex()]);
  progress = computed(() => this.currentGameIndex() + 1);
  total = computed(() => this.games().length);

  async ngOnInit() {
    await this.loadQuestions();
  }

  async loadQuestions() {
    try {
      this.isLoading.set(true);

      const response = await firstValueFrom(
        this.http.get<{ questions: any[], total: number }>(
          `${environment.apiUrl}/onboarding/games/questions`
        )
      );

      // Map backend questions to GameInstance format
      const mappedGames = response.questions.map(q => ({
        id: q.questionId,
        type: q.gameType,
        pillar: q.pillar,
        question: q.question
      }));

      this.games.set(mappedGames);
      this.isLoading.set(false);
      this.cdr.markForCheck();
    } catch (error) {
      console.error('Failed to load questions:', error);
      this.loadError.set('Failed to load questions. Please try again.');
      this.isLoading.set(false);
      this.cdr.markForCheck();
    }
  }

  onSliderAnswer(answer: SliderGameAnswer): void {
    this.saveAnswer(answer);
  }

  onRankingAnswer(answer: RankingGameAnswer): void {
    this.saveAnswer(answer);
  }

  onSwipeAnswer(answer: SwipeGameAnswer): void {
    this.saveAnswer(answer);
  }

  onSelectionAnswer(answer: SelectionGameAnswer): void {
    this.saveAnswer(answer);
  }

  private saveAnswer(answer: any): void {
    const current = this.currentGame();
    if (!current) return;

    this.answers.update(ans => [
      ...ans,
      {
        gameId: current.id,
        pillar: current.pillar,
        answer
      }
    ]);

    // Move to next game or complete
    const nextIndex = this.currentGameIndex() + 1;
    if (nextIndex < this.games().length) {
      this.currentGameIndex.set(nextIndex);
      this.cdr.markForCheck();
    } else {
      this.completeGames();
    }
  }

  private async completeGames(): Promise<void> {
    this.isComplete.set(true);
    console.log('All games complete! Answers:', this.answers());

    try {
      // Submit answers to backend (V2 endpoint with LLM scoring)
      const response = await firstValueFrom(
        this.http.post<any>(`${environment.apiUrl}/onboarding/games/submit-v2`, {
          answers: this.answers()
        })
      );

      console.log('Games submitted successfully:', response);

      // Store pillar results for results page
      if (response.pillars) {
        sessionStorage.setItem('pillarResults', JSON.stringify(response.pillars));
      }

      // Navigate to results page (not next onboarding step)
      await this.router.navigate(['/onboarding/games/results']);
    } catch (error) {
      console.error('Failed to submit games:', error);
      // TODO: Show error message to user
    }
  }
}
