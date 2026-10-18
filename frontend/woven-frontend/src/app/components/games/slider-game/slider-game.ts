import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GameProgress } from '../game-progress/game-progress';

export interface SliderGameQuestion {
  question: string;
  leftLabel: string;
  rightLabel: string;
}

export interface SliderGameAnswer {
  questionIndex: number;
  value: number;
}

@Component({
  selector: 'app-slider-game',
  imports: [CommonModule, FormsModule, GameProgress],
  templateUrl: './slider-game.html',
  styleUrl: './slider-game.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SliderGame {
  questions = input.required<SliderGameQuestion[]>();
  currentQuestionIndex = input<number>(0);
  totalQuestions = input<number>(6);

  answerSubmitted = output<SliderGameAnswer>();

  value = signal<number>(50); // Default to middle

  get currentQuestion(): SliderGameQuestion | undefined {
    return this.questions()[this.currentQuestionIndex()];
  }

  onNext(): void {
    this.answerSubmitted.emit({
      questionIndex: this.currentQuestionIndex(),
      value: this.value()
    });
  }
}
