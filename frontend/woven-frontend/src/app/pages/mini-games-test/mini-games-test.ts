import { ChangeDetectionStrategy, Component } from '@angular/core';
import { GameOrchestrator } from '../../components/games/game-orchestrator/game-orchestrator';

@Component({
  selector: 'app-mini-games-test',
  imports: [GameOrchestrator],
  templateUrl: './mini-games-test.html',
  styleUrl: './mini-games-test.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MiniGamesTest {

}
