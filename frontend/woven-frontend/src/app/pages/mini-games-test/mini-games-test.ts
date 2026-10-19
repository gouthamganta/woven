import { ChangeDetectionStrategy, Component } from '@angular/core';
import { GameOrchestrator } from '../../components/games/game-orchestrator/game-orchestrator';
import { OnboardingShellComponent } from '../onboarding/onboarding-shell';

@Component({
  selector: 'app-mini-games-test',
  imports: [GameOrchestrator, OnboardingShellComponent],
  template: `
    <woven-onboarding-shell>
      <app-game-orchestrator />
    </woven-onboarding-shell>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MiniGamesTest {

}
