import { Component, signal, OnInit, ChangeDetectionStrategy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

interface PillarResult {
  name: string;
  score: number;
  interpretation: string;
  revealed: boolean;
  index: number;
}

@Component({
  selector: 'app-games-results',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './games-results.component.html',
  styleUrls: ['./games-results.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class GamesResultsComponent implements OnInit {
  private router = inject(Router);
  private cdr = inject(ChangeDetectorRef);

  pillars = signal<PillarResult[]>([]);
  allRevealed = signal(false);

  ngOnInit() {
    this.loadResults();
  }

  private loadResults() {
    const resultsJson = sessionStorage.getItem('pillarResults');

    if (!resultsJson) {
      // No results, redirect back
      this.router.navigate(['/onboarding/games']);
      return;
    }

    const results = JSON.parse(resultsJson);

    const pillarData: PillarResult[] = [
      {
        name: 'Energy',
        score: Math.round((results.Energy?.score ?? 0.5) * 100),
        interpretation: results.Energy?.interpretation ?? 'You balance social and solo time',
        revealed: false,
        index: 0
      },
      {
        name: 'Stability',
        score: Math.round((results.Stability?.score ?? 0.5) * 100),
        interpretation: results.Stability?.interpretation ?? 'You mix structure with flexibility',
        revealed: false,
        index: 1
      },
      {
        name: 'Ambition',
        score: Math.round((results.Ambition?.score ?? 0.5) * 100),
        interpretation: results.Ambition?.interpretation ?? 'You balance achievement with other priorities',
        revealed: false,
        index: 2
      },
      {
        name: 'Curiosity',
        score: Math.round((results.Curiosity?.score ?? 0.5) * 100),
        interpretation: results.Curiosity?.interpretation ?? 'You explore new ideas when they interest you',
        revealed: false,
        index: 3
      },
      {
        name: 'Communication',
        score: Math.round((results.Communication?.score ?? 0.5) * 100),
        interpretation: results.Communication?.interpretation ?? 'You share openly when comfortable',
        revealed: false,
        index: 4
      },
      {
        name: 'Lifestyle',
        score: Math.round((results.Lifestyle?.score ?? 0.5) * 100),
        interpretation: results.Lifestyle?.interpretation ?? 'You enjoy a mix of activities',
        revealed: false,
        index: 5
      },
      {
        name: 'Values',
        score: Math.round((results.Values?.score ?? 0.5) * 100),
        interpretation: results.Values?.interpretation ?? 'You value meaningful connections',
        revealed: false,
        index: 6
      },
      {
        name: 'Affection',
        score: Math.round((results.Affection?.score ?? 0.5) * 100),
        interpretation: results.Affection?.interpretation ?? 'You show care in your own way',
        revealed: false,
        index: 7
      }
    ];

    this.pillars.set(pillarData);
    this.animateReveal();
  }

  private animateReveal() {
    const pillars = this.pillars();

    pillars.forEach((pillar, index) => {
      setTimeout(() => {
        pillar.revealed = true;
        this.pillars.set([...pillars]);
        this.cdr.markForCheck();

        // All revealed
        if (index === pillars.length - 1) {
          setTimeout(() => {
            this.allRevealed.set(true);
            this.cdr.markForCheck();
          }, 400);
        }
      }, index * 300); // 300ms delay per pillar
    });
  }

  continue() {
    // Clean up session storage
    sessionStorage.removeItem('pillarResults');

    // Navigate to next onboarding step
    this.router.navigate(['/onboarding/details']);
  }
}
