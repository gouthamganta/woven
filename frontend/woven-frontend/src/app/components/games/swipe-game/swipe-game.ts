import { ChangeDetectionStrategy, Component, input, output, signal, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface SwipeGameOption {
  text: string;
}

export interface SwipeGameQuestion {
  question: string;
  options: SwipeGameOption[];
}

export interface SwipeGameAnswer {
  questionIndex: number;
  responses: { option: string; rating: number }[]; // 1=Strongly Disagree, 5=Strongly Agree
}

@Component({
  selector: 'app-swipe-game',
  imports: [CommonModule],
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
  responses = signal<{ option: string; rating: number }[]>([]);

  get currentQuestion(): SwipeGameQuestion | undefined {
    return this.questions()[this.currentQuestionIndex()];
  }

  get currentOption(): SwipeGameOption | undefined {
    const question = this.currentQuestion;
    if (!question) return undefined;
    return question.options[this.currentCardIndex()];
  }

  rate(rating: number): void {
    const current = this.currentOption;
    if (!current) return;

    // Store rating for this option
    this.responses.update(resps => [...resps, { option: current.text, rating }]);

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
      responses: this.responses()
    });
    // Reset for next question
    this.currentCardIndex.set(0);
    this.responses.set([]);
  }
}
