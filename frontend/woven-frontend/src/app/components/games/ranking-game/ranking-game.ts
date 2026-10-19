import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';

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
  imports: [CommonModule, DragDropModule],
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

  drop(event: CdkDragDrop<string[]>): void {
    const items = [...this.rankedItems()];
    moveItemInArray(items, event.previousIndex, event.currentIndex);
    this.rankedItems.set(items);
  }

  onNext(): void {
    this.answerSubmitted.emit({
      questionIndex: this.currentQuestionIndex(),
      rankedItems: this.rankedItems()
    });
  }
}
