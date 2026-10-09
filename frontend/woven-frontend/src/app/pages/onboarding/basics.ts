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
          <div class="dateRow">
            <select class="dateSelect" [(ngModel)]="dobMonth" (change)="updateDob()">
              <option value="">Month</option>
              <option *ngFor="let m of months" [value]="m.val">{{ m.label }}</option>
            </select>
            <select class="dateSelect" [(ngModel)]="dobDay" (change)="updateDob()">
              <option value="">Day</option>
              <option *ngFor="let d of days" [value]="d">{{ d }}</option>
            </select>
            <select class="dateSelect" [(ngModel)]="dobYear" (change)="updateDob()">
              <option value="">Year</option>
              <option *ngFor="let y of years" [value]="y">{{ y }}</option>
            </select>
          </div>
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
          <select class="input" [(ngModel)]="cityText" (change)="citySelectionChanged()">
            <option value="">Select city</option>
            <option value="Hyderabad">Hyderabad</option>
          </select>
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
          <label class="label">Age range</label>
          <div class="ageRow">
            <div class="ageInput">
              <label class="ageLabel">Min</label>
              <div class="numberControl">
                <button class="numBtn" (click)="adjustAge('min', -1)">−</button>
                <input class="numInput" type="number" [(ngModel)]="ageMin" min="18" [max]="ageMax - 1" (input)="mark()"/>
                <button class="numBtn" (click)="adjustAge('min', 1)">+</button>
              </div>
            </div>
            <div class="ageInput">
              <label class="ageLabel">Max</label>
              <div class="numberControl">
                <button class="numBtn" (click)="adjustAge('max', -1)">−</button>
                <input class="numInput" type="number" [(ngModel)]="ageMax" [min]="ageMin + 1" max="80" (input)="mark()"/>
                <button class="numBtn" (click)="adjustAge('max', 1)">+</button>
              </div>
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

    .dateRow {
      display: grid;
      grid-template-columns: 2fr 1fr 1.2fr;
      gap: 10px;
    }

    .dateSelect {
      width: 100%;
      padding: 13px 14px;
      background: rgba(255,255,255,0.04);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      color: var(--text-primary);
      font-family: var(--font-ui);
      font-size: 15px;
      outline: none;
      cursor: pointer;
      transition: border-color 0.2s ease;
    }
    .dateSelect:focus { border-color: var(--gold-400); }
    .dateSelect option { background: var(--bg-surface); color: var(--text-primary); }

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

    .ageRow {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .ageInput {
      display: grid;
      gap: 8px;
    }

    .ageLabel {
      font-family: var(--font-ui);
      font-size: 11px;
      font-weight: 600;
      color: var(--text-muted);
      text-align: center;
    }

    .numberControl {
      display: grid;
      grid-template-columns: 40px 1fr 40px;
      align-items: center;
      background: rgba(255,255,255,0.04);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      overflow: hidden;
    }

    .numBtn {
      height: 45px;
      background: transparent;
      border: none;
      color: var(--gold-400);
      font-size: 20px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s ease;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .numBtn:hover { background: rgba(255,255,255,0.05); }
    .numBtn:active { background: rgba(255,255,255,0.08); }

    .numInput {
      width: 100%;
      padding: 13px 8px;
      background: transparent;
      border: none;
      border-left: 1px solid var(--border-subtle);
      border-right: 1px solid var(--border-subtle);
      color: var(--text-primary);
      font-family: var(--font-data);
      font-size: 16px;
      font-weight: 600;
      text-align: center;
      outline: none;
    }
    .numInput::-webkit-inner-spin-button,
    .numInput::-webkit-outer-spin-button {
      -webkit-appearance: none;
      margin: 0;
    }

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

  // Date dropdowns
  months = [
    { val: '01', label: 'January' }, { val: '02', label: 'February' }, { val: '03', label: 'March' },
    { val: '04', label: 'April' }, { val: '05', label: 'May' }, { val: '06', label: 'June' },
    { val: '07', label: 'July' }, { val: '08', label: 'August' }, { val: '09', label: 'September' },
    { val: '10', label: 'October' }, { val: '11', label: 'November' }, { val: '12', label: 'December' }
  ];
  days = Array.from({length: 31}, (_, i) => i + 1);
  years = Array.from({length: 83}, (_, i) => new Date().getFullYear() - 18 - i);

  firstName      = '';
  dob            = '';
  dobMonth       = '';
  dobDay         = '';
  dobYear        = '';
  gender         = '';
  pronouns_sel   = '';
  cityText       = '';
  distanceMiles  = 25;
  ageMin         = 21;
  ageMax         = 40;

  orientationSet  = new Set<string>();
  interestedInSet = new Set<string>();
  lookingForSet   = new Set<string>();

  selectedCity: { city: string; state: string; lat: number; lng: number } | null = null;

  loading = false;
  err = '';

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

  updateDob() {
    if (this.dobYear && this.dobMonth && this.dobDay) {
      this.dob = `${this.dobYear}-${this.dobMonth}-${this.dobDay.toString().padStart(2, '0')}`;
    } else {
      this.dob = '';
    }
    this.cdr.markForCheck();
  }

  citySelectionChanged() {
    if (this.cityText === 'Hyderabad') {
      this.selectedCity = { city: 'Hyderabad', state: 'Telangana', lat: 17.385, lng: 78.4867 };
    } else {
      this.selectedCity = null;
    }
    this.cdr.markForCheck();
  }

  adjustAge(type: 'min' | 'max', delta: number) {
    if (type === 'min') {
      const newMin = this.ageMin + delta;
      if (newMin >= 18 && newMin < this.ageMax) {
        this.ageMin = newMin;
      }
    } else {
      const newMax = this.ageMax + delta;
      if (newMax > this.ageMin && newMax <= 80) {
        this.ageMax = newMax;
      }
    }
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
