import { ChangeDetectionStrategy, Component, input, computed } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-game-progress',
  imports: [CommonModule],
  templateUrl: './game-progress.html',
  styleUrl: './game-progress.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameProgress {
  current = input.required<number>();
  total = input.required<number>();

  progress = computed(() => {
    const curr = this.current();
    const tot = this.total();
    return tot > 0 ? (curr / tot) * 100 : 0;
  });
}
