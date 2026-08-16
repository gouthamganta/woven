import { Component, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

const GENDERS = [
  { key: 'man',          label: 'Man' },
  { key: 'woman',        label: 'Woman' },
  { key: 'nonbinary',    label: 'Non-binary' },
  { key: 'transgender',  label: 'Transgender' },
  { key: 'genderfluid',  label: 'Gender-fluid' },
  { key: 'other',        label: 'Other' },
  { key: 'prefer_not',   label: 'Prefer not to say' },
];

const INTERESTS = [
  { key: 'men',      label: 'Men' },
  { key: 'women',    label: 'Women' },
  { key: 'nonbinary',label: 'Non-binary people' },
  { key: 'everyone', label: 'Everyone' },
];

const LOOKING_FOR = [
  { key: 'long_term',       label: 'Long-term' },
  { key: 'short_term',      label: 'Short-term' },
  { key: 'friendship',      label: 'Friendship' },
  { key: 'open_to_anything',label: 'Open to anything' },
];

const ORIENTATIONS = [
  { key: 'straight',    label: 'Straight' },
  { key: 'gay',         label: 'Gay' },
  { key: 'lesbian',     label: 'Lesbian' },
  { key: 'bisexual',    label: 'Bisexual' },
  { key: 'pansexual',   label: 'Pansexual' },
  { key: 'asexual',     label: 'Asexual' },
  { key: 'queer',       label: 'Queer' },
  { key: 'prefer_not',  label: 'Prefer not to say' },
];

const PRONOUNS = [
  { key: 'he_him',    label: 'He / Him' },
  { key: 'she_her',   label: 'She / Her' },
  { key: 'they_them', label: 'They / Them' },
  { key: 'other',     label: 'Other' },
];

@Component({
  selector: 'woven-onboarding-basics',
  standalone: true,
  imports: [CommonModule, FormsModule, OnboardingShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <woven-onboarding-shell
      title="The basics."
      subtitle="Just enough to get started — you can always edit later."
      [stepNumber]="2"
      [totalSteps]="8"
      stepLabel="Basics"
    >
      <div class="stack">

        <!-- Name -->
        <div class="field">
          <label class="label">First name</label>
          <input class="input" type="text" [(ngModel)]="firstName" placeholder="What do people call you?" maxlength="50"/>
        </div>

        <!-- Date of birth -->
        <div class="field">
          <label class="label">Date of birth</label>
          <input class="input" type="date" [(ngModel)]="dob" [max]="maxDob"/>
          <span class="hint" *ngIf="age !== null">{{ age }} years old</span>
          <span class="hint err" *ngIf="age !== null && age < 18">You must be 18 or older to join Woven.</span>
        </div>

        <div class="divider"></div>

        <!-- Gender -->
        <div class="field">
          <label class="label">Gender</label>
          <div class="pills">
            <button *ngFor="let g of genders" class="pill" [class.active]="gender === g.key" (click)="gender = g.key; mark()">
              {{ g.label }}
            </button>
          </div>
        </div>

        <!-- Pronouns -->
        <div class="field">
          <label class="label">Pronouns</label>
          <div class="pills">
            <button *ngFor="let p of pronouns" class="pill" [class.active]="pronouns_sel === p.key" (click)="pronouns_sel = p.key; mark()">
              {{ p.label }}
            </button>
          </div>
        </div>

        <!-- Orientation (optional) -->
        <div class="field">
          <label class="label">Sexual orientation <span class="opt">optional</span></label>
          <div class="pills">
            <button *ngFor="let o of orientations" class="pill" [class.active]="orientationSet.has(o.key)" (click)="toggleSet(orientationSet, o.key)">
              {{ o.label }}
            </button>
          </div>
        </div>

        <div class="divider"></div>

        <!-- Location -->
        <div class="field">
          <label class="label">Your city</label>
          <input class="input" type="text" [(ngModel)]="cityText" placeholder="City, State" (input)="cityChanged()"/>
          <div class="autocomplete" *ngIf="citySuggestions.length">
            <button *ngFor="let s of citySuggestions" class="suggestion" (click)="selectCity(s)">{{ s.label }}</button>
          </div>
        </div>

        <!-- Distance preference -->
        <div class="field">
          <label class="label">Distance — <strong class="val">{{ distanceMiles }} km</strong></label>
          <input class="slider" type="range" [(ngModel)]="distanceMiles" min="15" max="100" step="5" (input)="mark()"/>
          <div class="sliderRange"><span>15 km</span><span>100 km</span></div>
        </div>

        <div class="divider"></div>

        <!-- Interested in -->
        <div class="field">
          <label class="label">Interested in</label>
          <div class="pills">
            <button *ngFor="let i of interests" class="pill" [class.active]="interestedInSet.has(i.key)" (click)="toggleSet(interestedInSet, i.key)">
              {{ i.label }}
            </button>
          </div>
        </div>

        <!-- Looking for -->
        <div class="field">
          <label class="label">Looking for</label>
          <div class="pills">
            <button *ngFor="let l of lookingFor" class="pill" [class.active]="lookingForSet.has(l.key)" (click)="toggleSet(lookingForSet, l.key)">
              {{ l.label }}
            </button>
          </div>
        </div>

        <!-- Age range -->
        <div class="field">
          <label class="label">Age range — <strong class="val">{{ ageMin }}–{{ ageMax }}</strong></label>
          <div class="ageRow">
            <div class="ageSlider">
              <span class="ageLabel">Min</span>
              <input class="slider" type="range" [(ngModel)]="ageMin" min="18" [max]="ageMax - 1" step="1" (input)="mark()"/>
            </div>
            <div class="ageSlider">
              <span class="ageLabel">Max</span>
              <input class="slider" type="range" [(ngModel)]="ageMax" [min]="ageMin + 1" max="80" step="1" (input)="mark()"/>
            </div>
          </div>
        </div>

        <div class="divider"></div>

        <p class="err" *ngIf="err">{{ err }}</p>

        <button class="cta" (click)="next()" [disabled]="loading || !canProceed">
          <span *ngIf="!loading">Continue →</span>
          <span *ngIf="loading">Saving…</span>
        </button>

      </div>
    </woven-onboarding-shell>
  `,
  styles: [`
    .stack { display: grid; gap: 22px; }
    .divider { height: 1px; background: linear-gradient(90deg, transparent, var(--border-soft), transparent); }

    .field { display: grid; gap: 8px; }

    .label {
      font-family: var(--font-ui);
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.12em;
      text-transform: uppercase;
      color: var(--text-muted);
    }
    .opt { font-weight: 500; opacity: 0.6; text-transform: none; letter-spacing: 0; }

    .input {
      width: 100%;
      padding: 13px 14px;
      background: rgba(255,255,255,0.04);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      color: var(--text-primary);
      font-family: var(--font-ui);
      font-size: 15px;
      outline: none;
      transition: border-color 0.2s ease;
      box-sizing: border-box;
    }
    .input::placeholder { color: var(--text-dim); }
    .input:focus { border-color: var(--gold-400); }
    .input[type="date"]::-webkit-calendar-picker-indicator { filter: invert(0.6); }

    .hint {
      font-family: var(--font-ui);
      font-size: 12px;
      color: var(--text-muted);
    }
    .hint.err { color: var(--rose-300); }

    .pills { display: flex; flex-wrap: wrap; gap: 8px; }

    .pill {
      padding: 9px 16px;
      border: 1px solid var(--border-subtle);
      border-radius: 9999px;
      background: transparent;
      color: var(--text-muted);
      font-family: var(--font-ui);
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.15s ease;
    }
    .pill:hover { border-color: var(--border-soft); color: var(--text-secondary); }
    .pill.active {
      border-color: var(--gold-400);
      background: rgba(212, 160, 23, 0.10);
      color: var(--gold-300);
      font-weight: 600;
    }

    .autocomplete {
      background: var(--bg-elevated);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-lg);
      overflow: hidden;
    }
    .suggestion {
      width: 100%;
      text-align: left;
      padding: 12px 14px;
      background: transparent;
      border: none;
      border-bottom: 1px solid var(--border-subtle);
      color: var(--text-secondary);
      font-family: var(--font-ui);
      font-size: 14px;
      cursor: pointer;
      transition: background 0.15s ease;
    }
    .suggestion:last-child { border-bottom: none; }
    .suggestion:hover { background: rgba(255,255,255,0.05); }

    .slider {
      width: 100%;
      accent-color: var(--gold-400);
      cursor: pointer;
    }
    .sliderRange {
      display: flex;
      justify-content: space-between;
      font-family: var(--font-data);
      font-size: 11px;
      color: var(--text-dim);
    }
    .val { color: var(--gold-300); }

    .ageRow { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
    .ageSlider { display: grid; gap: 6px; }
    .ageLabel { font-family: var(--font-ui); font-size: 11px; color: var(--text-dim); }

    .cta {
      width: 100%;
      padding: 16px 24px;
      border: none;
      border-radius: var(--radius-xl);
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base);
      font-family: var(--font-ui);
      font-size: 15px;
      font-weight: 700;
      cursor: pointer;
      transition: opacity 0.2s ease, transform 0.15s ease;
      box-shadow: 0 4px 20px rgba(212, 160, 23, 0.28);
    }
    .cta:hover:not(:disabled) { opacity: 0.88; }
    .cta:active:not(:disabled) { transform: scale(0.96); }
    .cta:disabled { opacity: 0.4; cursor: not-allowed; }

    .err { font-size: 12px; color: var(--rose-300); }
  `],
})
export class BasicsOnboardingComponent {
  genders      = GENDERS;
  interests    = INTERESTS;
  lookingFor   = LOOKING_FOR;
  orientations = ORIENTATIONS;
  pronouns     = PRONOUNS;

  firstName      = '';
  dob            = '';
  gender         = '';
  pronouns_sel   = '';
  cityText       = '';
  distanceMiles  = 25;
  ageMin         = 21;
  ageMax         = 40;

  orientationSet  = new Set<string>();
  interestedInSet = new Set<string>();
  lookingForSet   = new Set<string>();

  citySuggestions: { label: string; city: string; state: string; lat: number; lng: number }[] = [];
  selectedCity: { city: string; state: string; lat: number; lng: number } | null = null;

  loading = false;
  err = '';

  get maxDob() {
    const d = new Date();
    d.setFullYear(d.getFullYear() - 18);
    return d.toISOString().split('T')[0];
  }

  get age(): number | null {
    if (!this.dob) return null;
    const diff = Date.now() - new Date(this.dob).getTime();
    return Math.floor(diff / (1000 * 60 * 60 * 24 * 365.25));
  }

  get canProceed(): boolean {
    return !!(
      this.firstName.trim() &&
      this.dob &&
      (this.age ?? 0) >= 18 &&
      this.gender &&
      this.interestedInSet.size > 0 &&
      this.selectedCity
    );
  }

  constructor(
    private onboarding: OnboardingService,
    private router: Router,
    private cdr: ChangeDetectorRef,
  ) {}

  mark() { this.cdr.markForCheck(); }

  toggleSet(set: Set<string>, key: string) {
    set.has(key) ? set.delete(key) : set.add(key);
    this.cdr.markForCheck();
  }

  cityChanged() {
    const q = this.cityText.trim();
    this.selectedCity = null;
    if (q.length < 2) { this.citySuggestions = []; this.cdr.markForCheck(); return; }
    // Minimal static suggestions — real implementation would call a geocoding API
    this.citySuggestions = [
      { label: `${q} (use this)`, city: q, state: '', lat: 0, lng: 0 },
    ];
    this.cdr.markForCheck();
  }

  selectCity(s: typeof this.citySuggestions[0]) {
    this.cityText = s.label !== `${s.city} (use this)` ? s.label : s.city;
    this.selectedCity = { city: s.city, state: s.state, lat: s.lat, lng: s.lng };
    this.citySuggestions = [];
    this.cdr.markForCheck();
  }

  async next() {
    if (!this.canProceed || !this.selectedCity) return;
    this.loading = true;
    this.err = '';
    this.cdr.markForCheck();
    try {
      const res = await firstValueFrom(this.onboarding.submitBasics({
        fullName: this.firstName.trim(),
        dateOfBirth: this.dob,
        gender: this.gender,
        interestedIn: [...this.interestedInSet],
        distanceMiles: this.distanceMiles,
        ageMin: this.ageMin,
        ageMax: this.ageMax,
        city: this.selectedCity.city,
        state: this.selectedCity.state,
        lat: this.selectedCity.lat,
        lng: this.selectedCity.lng,
        pronouns: this.pronouns_sel || undefined,
        orientation: this.orientationSet.size ? [...this.orientationSet] : undefined,
        lookingFor: this.lookingForSet.size ? [...this.lookingForSet] : undefined,
      }));
      this.router.navigateByUrl(res.nextRoute || '/onboarding/photos');
    } catch {
      this.err = 'Could not save. Please check your details and try again.';
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
    }
  }
}
