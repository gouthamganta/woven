import { ChangeDetectionStrategy, Component, input, output, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface SelectionOption {
  icon?: string;
  label: string;
  value: string;
}

export interface SelectionGameQuestion {
  question: string;
  subtitle: string;
  maxSelect: number;
  options: SelectionOption[];
}

export interface SelectionGameAnswer {
  questionIndex: number;
  selectedValues: string[];
}

@Component({
  selector: 'app-selection-game',
  imports: [CommonModule],
  templateUrl: './selection-game.html',
  styleUrl: './selection-game.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SelectionGame {
  questions = input.required<SelectionGameQuestion[]>();
  currentQuestionIndex = input<number>(0);
  totalQuestions = input<number>(6);

  answerSubmitted = output<SelectionGameAnswer>();

  selectedValues = signal<string[]>([]);

  get currentQuestion(): SelectionGameQuestion | undefined {
    return this.questions()[this.currentQuestionIndex()];
  }

  canProceed = computed(() => {
    const question = this.currentQuestion;
    if (!question) return false;
    return this.selectedValues().length === question.maxSelect;
  });

  isSelected(option: SelectionOption): boolean {
    return this.selectedValues().includes(option.value);
  }

  toggle(option: SelectionOption): void {
    const current = this.selectedValues();
    const question = this.currentQuestion;
    if (!question) return;

    if (current.includes(option.value)) {
      // Deselect
      this.selectedValues.set(current.filter(v => v !== option.value));
    } else {
      // Select only if under limit
      if (current.length < question.maxSelect) {
        this.selectedValues.set([...current, option.value]);
      }
      // If at limit, don't allow more selections (do nothing)
    }
  }

  onNext(): void {
    this.answerSubmitted.emit({
      questionIndex: this.currentQuestionIndex(),
      selectedValues: this.selectedValues()
    });
    // Reset for next question
    this.selectedValues.set([]);
  }
}
