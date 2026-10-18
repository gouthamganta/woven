import { ChangeDetectionStrategy, Component } from '@angular/core';
import { GameOrchestrator } from '../../components/games/game-orchestrator/game-orchestrator';

@Component({
  selector: 'app-games-onboarding',
  imports: [GameOrchestrator],
  template: `
    <div class="games-page">
      <app-game-orchestrator />
    </div>
  `,
  styles: [`
    .games-page {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 20px;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GamesOnboardingComponent {}
