import { ChangeDetectionStrategy, Component, input, output, signal, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { GameProgress } from '../game-progress/game-progress';

export interface SwipeGameOption {
  text: string;
}

export interface SwipeGameQuestion {
  question: string;
  options: SwipeGameOption[];
}

export interface SwipeGameAnswer {
  questionIndex: number;
  selectedOptions: string[]; // Options that got "Yes"
}

@Component({
  selector: 'app-swipe-game',
  imports: [CommonModule, GameProgress],
  templateUrl: './swipe-game.html',
  styleUrl: './swipe-game.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SwipeGame {
  private cdr = inject(ChangeDetectorRef);

  questions = input.required<SwipeGameQuestion[]>();
  currentQuestionIndex = input<number>(0);
  totalQuestions = input<number>(6);

  answerSubmitted = output<SwipeGameAnswer>();

  currentCardIndex = signal<number>(0);
  selectedOptions = signal<string[]>([]);

  get currentQuestion(): SwipeGameQuestion | undefined {
    return this.questions()[this.currentQuestionIndex()];
  }

  get currentOption(): SwipeGameOption | undefined {
    const question = this.currentQuestion;
    if (!question) return undefined;
    return question.options[this.currentCardIndex()];
  }

  swipe(direction: 'left' | 'right'): void {
    const current = this.currentOption;
    if (!current) return;

    if (direction === 'right') {
      // "Yes" - add to selected
      this.selectedOptions.update(opts => [...opts, current.text]);
    }

    // Move to next card
    const nextIndex = this.currentCardIndex() + 1;
    const question = this.currentQuestion;

    if (question && nextIndex < question.options.length) {
      this.currentCardIndex.set(nextIndex);
      this.cdr.markForCheck();
    } else {
      // All cards done - submit answer
      this.onComplete();
    }
  }

  onComplete(): void {
    this.answerSubmitted.emit({
      questionIndex: this.currentQuestionIndex(),
      selectedOptions: this.selectedOptions()
    });
    // Reset for next question
    this.currentCardIndex.set(0);
    this.selectedOptions.set([]);
  }
}
