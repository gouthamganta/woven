import { ChangeDetectionStrategy, Component, signal, computed, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
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

  // All 8 games (will be randomized on init)
  private readonly GAME_DEFINITIONS: GameInstance[] = [
    {
      id: 'energy-spectrum',
      type: 'slider',
      pillar: 'Energy',
      question: {
        question: 'Where do you recharge your energy?',
        leftLabel: 'Quiet time alone',
        rightLabel: 'Social activities'
      }
    },
    {
      id: 'value-auction',
      type: 'ranking',
      pillar: 'Values',
      question: {
        question: 'Rank these values by importance to you',
        items: ['Honesty', 'Adventure', 'Security', 'Growth', 'Connection']
      }
    },
    {
      id: 'conversation-starter',
      type: 'swipe',
      pillar: 'Communication',
      question: {
        question: 'Which topics excite you in conversation?',
        options: [
          { text: 'Philosophy and big ideas' },
          { text: 'Career and ambitions' },
          { text: 'Personal stories and experiences' },
          { text: 'Current events and news' },
          { text: 'Hobbies and interests' }
        ]
      }
    },
    {
      id: 'affection-language',
      type: 'selection',
      pillar: 'Affection',
      question: {
        question: 'How do you show affection?',
        subtitle: 'Pick 2',
        maxSelect: 2,
        options: [
          { icon: '💬', label: 'Words', value: 'words' },
          { icon: '⏰', label: 'Time', value: 'time' },
          { icon: '🎁', label: 'Gifts', value: 'gifts' },
          { icon: '🤝', label: 'Acts', value: 'acts' },
          { icon: '🤗', label: 'Touch', value: 'touch' }
        ]
      }
    },
    {
      id: 'lifestyle-puzzle',
      type: 'selection',
      pillar: 'Lifestyle',
      question: {
        question: 'What does your ideal weekend look like?',
        subtitle: 'Pick 3',
        maxSelect: 3,
        options: [
          { icon: '🏃', label: 'Outdoors', value: 'outdoors' },
          { icon: '🎨', label: 'Creative', value: 'creative' },
          { icon: '📚', label: 'Learning', value: 'learning' },
          { icon: '🎉', label: 'Social', value: 'social' },
          { icon: '🧘', label: 'Rest', value: 'rest' },
          { icon: '🏠', label: 'Home', value: 'home' }
        ]
      }
    },
    {
      id: 'ambition-ladder',
      type: 'ranking',
      pillar: 'Ambition',
      question: {
        question: 'Rank your life priorities',
        items: ['Career success', 'Relationships', 'Personal growth', 'Health & wellness', 'Financial security']
      }
    },
    {
      id: 'stability-compass',
      type: 'slider',
      pillar: 'Stability',
      question: {
        question: 'How do you approach planning your life?',
        leftLabel: 'Go with the flow',
        rightLabel: 'Detailed plans'
      }
    },
    {
      id: 'curiosity-explorer',
      type: 'swipe',
      pillar: 'Curiosity',
      question: {
        question: 'What kinds of things spark your curiosity?',
        options: [
          { text: 'How things work (science, tech)' },
          { text: 'Different cultures and places' },
          { text: 'Creative ideas and art' },
          { text: 'People and their stories' },
          { text: 'New skills and learning' }
        ]
      }
    }
  ];

  games = signal<GameInstance[]>([]);
  currentGameIndex = signal<number>(0);
  answers = signal<GameAnswer[]>([]);
  isComplete = signal<boolean>(false);

  currentGame = computed(() => this.games()[this.currentGameIndex()]);
  progress = computed(() => this.currentGameIndex() + 1);
  total = computed(() => this.games().length);

  ngOnInit(): void {
    // Randomize game order
    const shuffled = [...this.GAME_DEFINITIONS].sort(() => Math.random() - 0.5);
    this.games.set(shuffled);
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

  private completeGames(): void {
    this.isComplete.set(true);
    console.log('All games complete! Answers:', this.answers());
    // TODO: Navigate to results page or emit answers to parent
  }
}
