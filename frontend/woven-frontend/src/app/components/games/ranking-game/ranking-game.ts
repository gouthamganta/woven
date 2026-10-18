import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { GameProgress } from '../game-progress/game-progress';

export interface RankingGameQuestion {
  question: string;
  items: string[];
}

export interface RankingGameAnswer {
  questionIndex: number;
  rankedItems: string[];
}

@Component({
  selector: 'app-ranking-game',
  imports: [CommonModule, GameProgress],
  templateUrl: './ranking-game.html',
  styleUrl: './ranking-game.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RankingGame {
  questions = input.required<RankingGameQuestion[]>();
  currentQuestionIndex = input<number>(0);
  totalQuestions = input<number>(6);

  answerSubmitted = output<RankingGameAnswer>();

  rankedItems = signal<string[]>([]);
  selectedIndex = signal<number | null>(null);

  ngOnInit(): void {
    const current = this.currentQuestion;
    if (current) {
      this.rankedItems.set([...current.items]);
    }
  }

  get currentQuestion(): RankingGameQuestion | undefined {
    return this.questions()[this.currentQuestionIndex()];
  }

  moveUp(index: number): void {
    if (index === 0) return;
    const items = [...this.rankedItems()];
    [items[index], items[index - 1]] = [items[index - 1], items[index]];
    this.rankedItems.set(items);
  }

  moveDown(index: number): void {
    const items = this.rankedItems();
    if (index === items.length - 1) return;
    const newItems = [...items];
    [newItems[index], newItems[index + 1]] = [newItems[index + 1], newItems[index]];
    this.rankedItems.set(newItems);
  }

  onNext(): void {
    this.answerSubmitted.emit({
      questionIndex: this.currentQuestionIndex(),
      rankedItems: this.rankedItems()
    });
  }
}
