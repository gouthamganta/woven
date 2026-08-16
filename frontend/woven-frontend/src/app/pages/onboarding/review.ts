import { Component, OnInit, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService, ReviewResponse } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

@Component({
  selector: 'woven-onboarding-review',
  standalone: true,
  imports: [CommonModule, OnboardingShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <woven-onboarding-shell
      title="This is you."
      subtitle="Take a moment. When you're ready, let's go."
      [stepNumber]="8"
      [totalSteps]="9"
      stepLabel="Review"
    >
      <div class="stack" *ngIf="!loading && review">

        <!-- Photos preview -->
        <div class="section" *ngIf="photos?.length">
          <div class="sectionRow">
            <div class="sectionHead">Photos</div>
            <button class="editBtn" (click)="editStep('/onboarding/photos')">Edit</button>
          </div>
          <div class="photoStrip">
            <div *ngFor="let p of photos; let i = index" class="photoThumb" [class.primary]="i === 0">
              <img [src]="p.url" [alt]="'Photo ' + (i+1)"/>
              <span *ngIf="i === 0" class="primaryBadge">Primary</span>
            </div>
          </div>
        </div>

        <div class="divider"></div>

        <!-- Basics -->
        <div class="section">
          <div class="sectionRow">
            <div class="sectionHead">Basics</div>
            <button class="editBtn" (click)="editStep('/onboarding/basics')">Edit</button>
          </div>
          <div class="rows">
            <div class="row" *ngIf="basics?.fullName"><span class="rowKey">Name</span><span class="rowVal">{{ basics.fullName }}</span></div>
            <div class="row" *ngIf="basics?.gender"><span class="rowKey">Gender</span><span class="rowVal">{{ basics.gender }}</span></div>
            <div class="row" *ngIf="basics?.city"><span class="rowKey">Location</span><span class="rowVal">{{ basics.city }}</span></div>
            <div class="row" *ngIf="basics?.distanceMiles"><span class="rowKey">Distance</span><span class="rowVal">{{ basics.distanceMiles }} km</span></div>
            <div class="row" *ngIf="basics?.interestedIn"><span class="rowKey">Interested in</span><span class="rowVal">{{ formatList(basics.interestedIn) }}</span></div>
          </div>
        </div>

        <div class="divider"></div>

        <!-- Intent -->
        <div class="section">
          <div class="sectionRow">
            <div class="sectionHead">Intent</div>
            <button class="editBtn" (click)="editStep('/onboarding/intent')">Edit</button>
          </div>
          <div class="rows">
            <div class="row" *ngIf="intent?.primaryIntent"><span class="rowKey">Here for</span><span class="rowVal">{{ intent.primaryIntent }}</span></div>
            <div class="row" *ngIf="intent?.reflectionSentence"><span class="rowKey">Reflection</span><span class="rowVal rowWrap">{{ intent.reflectionSentence }}</span></div>
          </div>
        </div>

        <div class="divider"></div>

        <!-- Bio -->
        <div class="section" *ngIf="bio">
          <div class="sectionRow">
            <div class="sectionHead">Bio</div>
            <button class="editBtn" (click)="editStep('/onboarding/details')">Edit</button>
          </div>
          <p class="bioText">{{ bio }}</p>
        </div>

        <div class="divider" *ngIf="bio"></div>

        <!-- Foundational done marker -->
        <div class="section">
          <div class="sectionRow">
            <div class="sectionHead">Five questions</div>
            <button class="editBtn" (click)="editStep('/onboarding/foundational')">Edit</button>
          </div>
          <p class="doneNote">✓ Answered — these power your compatibility matches.</p>
        </div>

        <div class="divider"></div>

        <p class="finalNote">Everything here is yours to change, any time. This is just the start.</p>

        <button class="cta" (click)="confirm()" [disabled]="confirming">
          <span *ngIf="!confirming">This is me — let's go →</span>
          <span *ngIf="confirming">Finishing up…</span>
        </button>

        <p class="err" *ngIf="err">{{ err }}</p>

      </div>

      <div class="loadState" *ngIf="loading">
        <div class="loadPulse"></div>
        <p>Loading your profile…</p>
      </div>

    </woven-onboarding-shell>
  `,
  styles: [`
    .stack { display: grid; gap: 20px; }
    .divider { height: 1px; background: linear-gradient(90deg, transparent, var(--border-soft), transparent); }

    .section { display: grid; gap: 12px; }
    .sectionRow { display: flex; justify-content: space-between; align-items: center; }
    .sectionHead { font-family: var(--font-display); font-size: 17px; font-weight: 400; color: var(--text-primary); }

    .editBtn {
      padding: 5px 12px; border: 1px solid var(--border-subtle); border-radius: 9999px;
      background: transparent; color: var(--text-muted); font-family: var(--font-ui);
      font-size: 11px; font-weight: 600; cursor: pointer; transition: all 0.15s ease;
    }
    .editBtn:hover { border-color: var(--gold-400); color: var(--gold-300); }

    .photoStrip { display: flex; gap: 8px; overflow-x: auto; }
    .photoThumb {
      position: relative; flex: 0 0 auto; width: 80px; height: 80px;
      border-radius: var(--radius-lg); overflow: hidden;
      border: 1px solid var(--border-subtle);
    }
    .photoThumb img { width: 100%; height: 100%; object-fit: cover; }
    .photoThumb.primary { border-color: var(--gold-400); box-shadow: 0 0 0 2px rgba(212,160,23,0.25); }
    .primaryBadge {
      position: absolute; bottom: 4px; left: 50%; transform: translateX(-50%);
      background: rgba(0,0,0,0.7); color: var(--gold-300); font-size: 9px;
      font-family: var(--font-ui); font-weight: 700; padding: 2px 6px;
      border-radius: 9999px; white-space: nowrap;
    }

    .rows { display: grid; gap: 8px; }
    .row { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; }
    .rowKey { font-family: var(--font-ui); font-size: 12px; color: var(--text-muted); flex: 0 0 100px; }
    .rowVal { font-family: var(--font-ui); font-size: 13px; color: var(--text-secondary); text-align: right; }
    .rowWrap { text-align: left; flex: 1; line-height: 1.5; }

    .bioText { font-family: var(--font-ui); font-size: 14px; color: var(--text-secondary); line-height: 1.6; }

    .doneNote { font-family: var(--font-ui); font-size: 13px; color: var(--text-muted); }

    .finalNote {
      font-family: var(--font-ui); font-size: 12px; color: var(--text-dim);
      font-style: italic; text-align: center; line-height: 1.5;
    }

    .cta {
      width: 100%; padding: 16px 24px; border: none; border-radius: var(--radius-xl);
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base); font-family: var(--font-ui); font-size: 15px; font-weight: 700;
      cursor: pointer; transition: opacity 0.2s ease, transform 0.15s ease;
      box-shadow: 0 4px 20px rgba(212,160,23,0.28);
    }
    .cta:hover:not(:disabled) { opacity: 0.88; }
    .cta:active:not(:disabled) { transform: scale(0.96); }
    .cta:disabled { opacity: 0.4; cursor: not-allowed; }

    .err { font-size: 12px; color: var(--rose-300); text-align: center; }

    .loadState {
      text-align: center; padding: 40px 0; display: grid; gap: 16px; justify-items: center;
    }
    .loadPulse {
      width: 40px; height: 40px; border-radius: 50%;
      border: 2px solid var(--border-soft); border-top-color: var(--gold-400);
      animation: spin 1s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .loadState p { font-family: var(--font-ui); font-size: 13px; color: var(--text-muted); }
  `],
})
export class ReviewOnboardingComponent implements OnInit {
  review: ReviewResponse | null = null;
  loading = true;
  confirming = false;
  err = '';

  get basics()  { return this.review?.basics  ?? this.review?.self?.basics  ?? this.review; }
  get intent()  { return this.review?.intent  ?? this.review?.self?.intent  ?? null; }
  get photos()  { return this.review?.photos  ?? this.review?.self?.photos  ?? []; }
  get bio()     { return this.review?.bio     ?? this.review?.self?.bio     ?? ''; }

  constructor(
    private onboarding: OnboardingService,
    private router: Router,
    private cdr: ChangeDetectorRef,
  ) {}

  async ngOnInit() {
    try {
      this.review = await firstValueFrom(this.onboarding.getReview());
    } catch {
      this.err = 'Could not load your profile preview.';
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
    }
  }

  formatList(val: any): string {
    if (Array.isArray(val)) return val.join(', ');
    if (typeof val === 'string') {
      try { return JSON.parse(val).join(', '); } catch { return val; }
    }
    return '';
  }

  editStep(route: string) { this.router.navigateByUrl(route); }

  async confirm() {
    this.confirming = true; this.err = ''; this.cdr.markForCheck();
    try {
      const res = await firstValueFrom(this.onboarding.completeOnboarding());
      this.router.navigateByUrl(res.nextRoute || '/onboarding/start');
    } catch {
      this.err = 'Something went wrong. Please try again.';
    } finally {
      this.confirming = false; this.cdr.markForCheck();
    }
  }
}
