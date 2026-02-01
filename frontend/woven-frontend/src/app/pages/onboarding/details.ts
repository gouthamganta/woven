import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

type VisibilityLevel = 'Public' | 'MatchingOnly' | 'Private';

type OptionalFieldDto = {
  key: string;
  value: string;
  visibility: VisibilityLevel;
};

// ✅ keys we allow for visibility toggles (Step 3)
type DetailVisKey =
  | 'children'
  | 'zodiac'
  | 'diet'
  | 'pets'
  | 'hometown'
  | 'hobbies'
  | 'languages';

@Component({
  selector: 'app-details-onboarding',
  standalone: true,
  imports: [CommonModule, FormsModule, OnboardingShellComponent],
  template: `
  <woven-onboarding-shell
    [title]="shellTitle"
    [subtitle]="shellSubtitle"
    [stepNumber]="step"
    [totalSteps]="3"
    [stepLabel]="'Details'"
  >
    <div class="error" *ngIf="error">{{ error }}</div>

    <!-- Step 1: Bio -->
    <ng-container *ngIf="step === 1">
      <div class="block">
        <label class="label">Bio <span class="req">*</span></label>
        <textarea
          [(ngModel)]="bio"
          [maxlength]="bioMax"
          rows="5"
          class="textarea"
          placeholder="A few lines that feel like you. Simple is perfect."
        ></textarea>

        <div class="row">
          <div class="mini">Max {{ bioMax }} characters.</div>
          <div class="count">{{ (bio || '').length }}/{{ bioMax }}</div>
        </div>

        <div class="helper">
          Think “enough to start a good conversation,” not a full autobiography.
        </div>
      </div>

      <div class="actions">
        <button class="ghost" (click)="back()" [disabled]="saving">Back</button>
        <button class="primary" (click)="next()" [disabled]="!bioValid || saving">Next</button>
      </div>
    </ng-container>

    <!-- Step 2: Dating preferences (Matching-only) -->
    <ng-container *ngIf="step === 2">
      <div class="notice">
        These are your <b>dating preferences</b>. They are used for matching and are not shown publicly.
      </div>

      <div class="grid">
        <div class="block">
          <label class="label">Preferred ethnicity (optional)</label>
          <select class="input" [(ngModel)]="prefEthnicity">
            <option value="">No preference</option>
            <option *ngFor="let o of ethnicityOptions" [value]="o">{{ o }}</option>
          </select>
        </div>

        <div class="block">
          <label class="label">Preferred religion (optional)</label>
          <select class="input" [(ngModel)]="prefReligion">
            <option value="">No preference</option>
            <option *ngFor="let o of religionOptions" [value]="o">{{ o }}</option>
          </select>
        </div>

        <div class="block">
          <label class="label">Preferred height (optional)</label>
          <select class="input" [(ngModel)]="prefHeight">
            <option value="">No preference</option>
            <option *ngFor="let o of heightOptions" [value]="o">{{ o }}</option>
          </select>
        </div>

        <div class="block">
          <label class="label">Preferred workout (optional)</label>
          <select class="input" [(ngModel)]="prefWorkout">
            <option value="">No preference</option>
            <option *ngFor="let o of workoutOptions" [value]="o">{{ o }}</option>
          </select>
        </div>

        <div class="block">
          <label class="label">Preferred smoking (optional)</label>
          <select class="input" [(ngModel)]="prefSmoking">
            <option value="">No preference</option>
            <option *ngFor="let o of smokingOptions" [value]="o">{{ o }}</option>
          </select>
        </div>

        <div class="block">
          <label class="label">Preferred drinking (optional)</label>
          <select class="input" [(ngModel)]="prefDrinking">
            <option value="">No preference</option>
            <option *ngFor="let o of drinkingOptions" [value]="o">{{ o }}</option>
          </select>
        </div>
      </div>

      <div class="block">
        <label class="label">Preferred work / profession (optional)</label>
        <input class="input" [(ngModel)]="prefWork" placeholder="e.g., Tech, Healthcare, Trades, Student…" />
        <div class="mini">Keep it broad. This helps matching, not filtering people out harshly.</div>
      </div>

      <div class="actions">
        <button class="ghost" (click)="back()" [disabled]="saving">Back</button>
        <button class="ghost" (click)="skipStep2()" [disabled]="saving">Skip for now</button>
        <button class="primary" (click)="next()" [disabled]="saving">Next</button>
      </div>
    </ng-container>

    <!-- Step 3: Your details + weekly vibe (with visibility toggles) -->
    <ng-container *ngIf="step === 3">
      <div class="notice">
        These are <b>your profile details</b>. You can control visibility per item.
      </div>

      <div class="grid">
        <!-- Children -->
        <div class="block">
          <label class="label">Children (optional)</label>
          <select class="input" [(ngModel)]="children">
            <option value="">Select</option>
            <option *ngFor="let o of childrenOptions" [value]="o">{{ o }}</option>
          </select>
          <div class="visRow">
            <div class="mini">Visibility</div>
            <div class="seg">
              <button type="button" class="segBtn" [class.on]="detailVis.children==='Public'" (click)="setVis('children','Public')">Public</button>
              <button type="button" class="segBtn" [class.on]="detailVis.children==='Private'" (click)="setVis('children','Private')">Private</button>
              <button type="button" class="segBtn" [class.on]="detailVis.children==='MatchingOnly'" (click)="setVis('children','MatchingOnly')">Matching</button>
            </div>
          </div>
        </div>

        <!-- Zodiac -->
        <div class="block">
          <label class="label">Zodiac (optional)</label>
          <select class="input" [(ngModel)]="zodiac">
            <option value="">Select</option>
            <option *ngFor="let o of zodiacOptions" [value]="o">{{ o }}</option>
          </select>
          <div class="visRow">
            <div class="mini">Visibility</div>
            <div class="seg">
              <button type="button" class="segBtn" [class.on]="detailVis.zodiac==='Public'" (click)="setVis('zodiac','Public')">Public</button>
              <button type="button" class="segBtn" [class.on]="detailVis.zodiac==='Private'" (click)="setVis('zodiac','Private')">Private</button>
              <button type="button" class="segBtn" [class.on]="detailVis.zodiac==='MatchingOnly'" (click)="setVis('zodiac','MatchingOnly')">Matching</button>
            </div>
          </div>
        </div>

        <!-- Diet -->
        <div class="block">
          <label class="label">Diet (optional)</label>
          <select class="input" [(ngModel)]="diet">
            <option value="">Select</option>
            <option *ngFor="let o of dietOptions" [value]="o">{{ o }}</option>
          </select>
          <div class="visRow">
            <div class="mini">Visibility</div>
            <div class="seg">
              <button type="button" class="segBtn" [class.on]="detailVis.diet==='Public'" (click)="setVis('diet','Public')">Public</button>
              <button type="button" class="segBtn" [class.on]="detailVis.diet==='Private'" (click)="setVis('diet','Private')">Private</button>
              <button type="button" class="segBtn" [class.on]="detailVis.diet==='MatchingOnly'" (click)="setVis('diet','MatchingOnly')">Matching</button>
            </div>
          </div>
        </div>

        <!-- Pets -->
        <div class="block">
          <label class="label">Pets (optional)</label>
          <input class="input" [(ngModel)]="pets" placeholder="Dog / Cat / None / Future pet parent" />
          <div class="visRow">
            <div class="mini">Visibility</div>
            <div class="seg">
              <button type="button" class="segBtn" [class.on]="detailVis.pets==='Public'" (click)="setVis('pets','Public')">Public</button>
              <button type="button" class="segBtn" [class.on]="detailVis.pets==='Private'" (click)="setVis('pets','Private')">Private</button>
              <button type="button" class="segBtn" [class.on]="detailVis.pets==='MatchingOnly'" (click)="setVis('pets','MatchingOnly')">Matching</button>
            </div>
          </div>
        </div>

        <!-- Hometown -->
        <div class="block">
          <label class="label">Hometown (optional)</label>
          <input class="input" [(ngModel)]="hometown" placeholder="Austin, TX" />
          <div class="visRow">
            <div class="mini">Visibility</div>
            <div class="seg">
              <button type="button" class="segBtn" [class.on]="detailVis.hometown==='Public'" (click)="setVis('hometown','Public')">Public</button>
              <button type="button" class="segBtn" [class.on]="detailVis.hometown==='Private'" (click)="setVis('hometown','Private')">Private</button>
              <button type="button" class="segBtn" [class.on]="detailVis.hometown==='MatchingOnly'" (click)="setVis('hometown','MatchingOnly')">Matching</button>
            </div>
          </div>
        </div>

        <!-- Hobbies -->
        <div class="block">
          <label class="label">Hobbies (optional)</label>
          <input class="input" [(ngModel)]="hobbies" placeholder="Gym, hiking, cooking, movies…" />
          <div class="visRow">
            <div class="mini">Visibility</div>
            <div class="seg">
              <button type="button" class="segBtn" [class.on]="detailVis.hobbies==='Public'" (click)="setVis('hobbies','Public')">Public</button>
              <button type="button" class="segBtn" [class.on]="detailVis.hobbies==='Private'" (click)="setVis('hobbies','Private')">Private</button>
              <button type="button" class="segBtn" [class.on]="detailVis.hobbies==='MatchingOnly'" (click)="setVis('hobbies','MatchingOnly')">Matching</button>
            </div>
          </div>
        </div>
      </div>

      <!-- Languages -->
      <div class="block">
        <label class="label">Languages (optional)</label>
        <div class="chips">
          <button
            type="button"
            class="chip"
            *ngFor="let lang of languageOptions"
            [class.active]="languages.includes(lang)"
            (click)="toggleLanguage(lang)"
          >
            {{ lang }}
          </button>
        </div>

        <div class="visRow" style="margin-top:10px;">
          <div class="mini">Visibility</div>
          <div class="seg">
            <button type="button" class="segBtn" [class.on]="detailVis.languages==='Public'" (click)="setVis('languages','Public')">Public</button>
            <button type="button" class="segBtn" [class.on]="detailVis.languages==='Private'" (click)="setVis('languages','Private')">Private</button>
            <button type="button" class="segBtn" [class.on]="detailVis.languages==='MatchingOnly'" (click)="setVis('languages','MatchingOnly')">Matching</button>
          </div>
        </div>

        <div class="mini">Pick as many as you want.</div>
      </div>

      <!-- Weekly vibe -->
      <div class="block">
        <label class="label">Weekly vibe (optional)</label>
        <textarea
          [(ngModel)]="weeklyVibe"
          (ngModelChange)="onVibeChange()"
          [maxlength]="vibeMax"
          rows="3"
          class="textarea"
          placeholder="What’s your week feeling like?"
        ></textarea>

        <div class="row">
          <div class="mini">Expires in 7 days.</div>
          <div class="count">{{ (weeklyVibe || '').length }}/{{ vibeMax }}</div>
        </div>

        <button class="miniBtn" type="button" (click)="weeklyVibe=''" [disabled]="saving">
          Clear
        </button>
      </div>

      <div class="actions">
        <button class="ghost" (click)="back()" [disabled]="saving">Back</button>
        <button class="ghost" (click)="skipStep3()" [disabled]="saving">Skip for now</button>
        <button class="primary" (click)="save(false)" [disabled]="saving || !bioValid">
          {{ saving ? 'Saving…' : 'Continue' }}
        </button>
      </div>
    </ng-container>
  </woven-onboarding-shell>
  `,
  styles: [`
    /* ===== Error ===== */
    .error {
      margin: 8px 0 16px;
      padding: 12px 14px;
      border-radius: 12px;
      background: rgba(176, 0, 32, 0.06);
      border: 1px solid rgba(176, 0, 32, 0.12);
      color: #b00020;
      font-size: 13px;
      font-weight: 500;
      line-height: 1.4;
    }

    /* ===== Notice ===== */
    .notice {
      margin: 8px 0 20px;
      padding: 14px 16px;
      border-radius: 14px;
      border: 1px solid rgba(0, 0, 0, 0.08);
      background: rgba(0, 0, 0, 0.02);
      font-size: 13px;
      font-weight: 500;
      line-height: 1.45;
      color: #0f0f0f;
    }

    .notice b {
      font-weight: 700;
    }

    /* ===== Block ===== */
    .block {
      margin: 16px 0;
    }

    /* ===== Label ===== */
    .label {
      font-weight: 650;
      font-size: 12px;
      display: block;
      margin-bottom: 10px;
      color: #0f0f0f;
      letter-spacing: 0.01em;
    }

    .req {
      color: #b00020;
      margin-left: 2px;
    }

    /* ===== Inputs ===== */
    .input, .textarea {
      width: 100%;
      border-radius: 12px;
      border: 1px solid rgba(0, 0, 0, 0.1);
      background: rgba(255, 255, 255, 0.9);
      padding: 12px 14px;
      outline: none;
      font-size: 14px;
      font-weight: 500;
      color: #0f0f0f;
      transition: all 0.2s ease;
    }

    .input::placeholder, .textarea::placeholder {
      color: #0f0f0f;
      opacity: 0.4;
    }

    .input:focus, .textarea:focus {
      border-color: #0f0f0f;
      box-shadow: 0 0 0 3px rgba(15, 15, 15, 0.06);
    }

    select.input {
      cursor: pointer;
      appearance: none;
      background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%23666' stroke-width='2'%3E%3Cpath d='M6 9l6 6 6-6'/%3E%3C/svg%3E");
      background-repeat: no-repeat;
      background-position: right 12px center;
      padding-right: 36px;
    }

    .textarea {
      resize: vertical;
      min-height: 100px;
      line-height: 1.5;
      font-family: inherit;
    }

    /* ===== Row ===== */
    .row {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      margin-top: 8px;
      align-items: center;
    }

    /* ===== Mini Text ===== */
    .mini {
      font-size: 11px;
      font-weight: 600;
      color: #0f0f0f;
      opacity: 0.45;
    }

    .count {
      font-size: 11px;
      font-weight: 700;
      color: #0f0f0f;
      opacity: 0.5;
    }

    /* ===== Helper ===== */
    .helper {
      margin-top: 10px;
      font-size: 12px;
      font-weight: 450;
      color: #0f0f0f;
      opacity: 0.55;
      line-height: 1.45;
    }

    /* ===== Grid ===== */
    .grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    @media (max-width: 560px) {
      .grid {
        grid-template-columns: 1fr;
        gap: 12px;
      }
    }

    /* ===== Actions ===== */
    .actions {
      margin-top: 24px;
      display: flex;
      justify-content: space-between;
      gap: 10px;
      align-items: center;
      flex-wrap: wrap;
    }

    /* ===== Buttons ===== */
    .ghost, .primary {
      border-radius: 12px;
      border: 1px solid rgba(0, 0, 0, 0.1);
      padding: 14px 18px;
      min-height: 44px;
      font-weight: 650;
      font-size: 14px;
      cursor: pointer;
      background: rgba(255, 255, 255, 0.8);
      color: #0f0f0f;
      transition: all 0.2s ease;
    }

    .ghost:hover:not(:disabled) {
      background: rgba(255, 255, 255, 1);
      border-color: rgba(0, 0, 0, 0.15);
    }

    .primary {
      background: linear-gradient(135deg, #0f0f0f 0%, #1a1a1a 100%);
      color: #fff;
      border-color: transparent;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
    }

    .primary:hover:not(:disabled) {
      transform: translateY(-1px);
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
    }

    .primary:active:not(:disabled) {
      transform: translateY(0);
    }

    button:disabled {
      opacity: 0.5;
      cursor: not-allowed;
      transform: none !important;
    }

    .ghost:focus-visible, .primary:focus-visible {
      outline: 2px solid #0f0f0f;
      outline-offset: 2px;
    }

    /* ===== Chips ===== */
    .chips {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      margin-top: 8px;
    }

    .chip {
      border-radius: 100px;
      border: 1px solid rgba(0, 0, 0, 0.1);
      background: rgba(255, 255, 255, 0.8);
      padding: 12px 18px;
      min-height: 44px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      color: #0f0f0f;
      transition: all 0.2s ease;
    }

    .chip:hover:not(.active) {
      border-color: rgba(0, 0, 0, 0.2);
      background: rgba(255, 255, 255, 1);
    }

    .chip.active {
      background: linear-gradient(135deg, #0f0f0f 0%, #1a1a1a 100%);
      color: #fff;
      border-color: transparent;
      box-shadow: 0 2px 6px rgba(0, 0, 0, 0.12);
    }

    .chip:focus-visible {
      outline: 2px solid #0f0f0f;
      outline-offset: 2px;
    }

    /* ===== Mini Button ===== */
    .miniBtn {
      margin-top: 10px;
      border-radius: 12px;
      border: 1px solid rgba(0, 0, 0, 0.1);
      padding: 12px 16px;
      min-height: 44px;
      font-weight: 650;
      background: rgba(255, 255, 255, 0.8);
      cursor: pointer;
      font-size: 13px;
      color: #0f0f0f;
      transition: all 0.2s ease;
    }

    .miniBtn:hover:not(:disabled) {
      background: rgba(255, 255, 255, 1);
      border-color: rgba(0, 0, 0, 0.15);
    }

    /* ===== Visibility Row ===== */
    .visRow {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;
      margin-top: 12px;
      padding-top: 10px;
      border-top: 1px solid rgba(0, 0, 0, 0.05);
    }

    /* ===== Segment Buttons ===== */
    .seg {
      display: flex;
      gap: 4px;
      flex-wrap: wrap;
      justify-content: flex-end;
    }

    .segBtn {
      border-radius: 100px;
      border: 1px solid rgba(0, 0, 0, 0.1);
      background: rgba(255, 255, 255, 0.8);
      padding: 10px 14px;
      min-height: 44px;
      font-weight: 650;
      font-size: 11px;
      cursor: pointer;
      color: #0f0f0f;
      transition: all 0.2s ease;
      text-transform: uppercase;
      letter-spacing: 0.03em;
    }

    .segBtn:hover:not(.on) {
      background: rgba(255, 255, 255, 1);
      border-color: rgba(0, 0, 0, 0.15);
    }

    .segBtn.on {
      background: linear-gradient(135deg, #0f0f0f 0%, #1a1a1a 100%);
      color: #fff;
      border-color: transparent;
    }

    .segBtn:focus-visible {
      outline: 2px solid #0f0f0f;
      outline-offset: 2px;
    }

    /* ===== Mobile ===== */
    @media (max-width: 480px) {
      .visRow {
        flex-direction: column;
        align-items: flex-start;
        gap: 10px;
      }

      .seg {
        justify-content: flex-start;
      }

      .segBtn {
        padding: 10px 14px;
        min-height: 44px;
        font-size: 11px;
      }

      .chips {
        gap: 8px;
      }

      .chip {
        padding: 12px 16px;
        min-height: 44px;
        font-size: 13px;
      }
    }
  `]
})
export class DetailsOnboardingComponent {
  step: 1 | 2 | 3 = 1;

  saving = false;
  error = '';

  bioMax = 200;
  vibeMax = 120;

  bio = '';
  weeklyVibe = '';

  // ✅ Step 2: dating preferences (stored as pref_* keys; backend forces MatchingOnly)
  prefEthnicity = '';
  prefReligion = '';
  prefHeight = '';
  prefWorkout = '';
  prefSmoking = '';
  prefDrinking = '';
  prefWork = '';

  // ✅ Step 3: your details (stored as detail keys; visibility controlled here)
  children = '';
  zodiac = '';
  diet = '';
  pets = '';
  hometown = '';
  hobbies = '';
  languages: string[] = [];

  // ✅ Per-detail visibility settings (defaults -> Public)
  detailVis: Record<DetailVisKey, VisibilityLevel> = {
    children: 'Public',
    zodiac: 'Public',
    diet: 'Public',
    pets: 'Public',
    hometown: 'Public',
    hobbies: 'Public',
    languages: 'Public',
  };

  ethnicityOptions = ['Asian', 'Black / African', 'Hispanic / Latino', 'Middle Eastern', 'Native / Indigenous', 'White', 'Mixed', 'Other', 'Prefer not to say'];
  religionOptions = ['Christian', 'Muslim', 'Hindu', 'Sikh', 'Buddhist', 'Jewish', 'Spiritual', 'Agnostic', 'Atheist', 'Other', 'Prefer not to say'];
  childrenOptions = ['No', 'Someday', 'Have & want more', 'Have & don’t want more', 'Prefer not to say'];
  zodiacOptions = ['Aries','Taurus','Gemini','Cancer','Leo','Virgo','Libra','Scorpio','Sagittarius','Capricorn','Aquarius','Pisces','Prefer not to say'];
  dietOptions = ['Anything','Vegetarian','Vegan','Pescatarian','Halal','Kosher','Keto','Gluten-free','Other','Prefer not to say'];
  workoutOptions = ['Never','Sometimes','Often','Daily','Prefer not to say'];
  smokingOptions = ['No', 'Sometimes', 'Yes', 'Prefer not to say'];
  drinkingOptions = ['No', 'Sometimes', 'Socially', 'Yes', 'Prefer not to say'];
  languageOptions = ['English','Spanish','Hindi','Telugu','Tamil','French','German','Arabic','Chinese','Japanese','Korean','Portuguese','Other'];

  heightOptions = [
    '4’10”','4’11”','5’0”','5’1”','5’2”','5’3”','5’4”','5’5”','5’6”','5’7”','5’8”','5’9”',
    '5’10”','5’11”','6’0”','6’1”','6’2”','6’3”','6’4”','6’5”','6’6”','6’7”','6’8”',
    'Prefer not to say'
  ];

  constructor(private onboarding: OnboardingService, private router: Router) {}

  get shellTitle() {
    if (this.step === 1) return 'A quick bio';
    if (this.step === 2) return 'Dating preferences';
    return 'Your details & vibe';
  }

  get shellSubtitle() {
    if (this.step === 1) return 'A short bio helps people start conversations naturally.';
    if (this.step === 2) return 'Optional filters to improve match quality (matching-only).';
    return 'Optional details with visibility controls + your weekly vibe.';
  }

  get bioValid() {
    return (this.bio || '').trim().length > 0 && (this.bio || '').length <= this.bioMax;
  }

  setVis(key: DetailVisKey, v: VisibilityLevel) {
    this.detailVis[key] = v;
  }

  toggleLanguage(lang: string) {
    const i = this.languages.indexOf(lang);
    if (i >= 0) this.languages.splice(i, 1);
    else this.languages.push(lang);
  }

  onVibeChange() {
    if ((this.weeklyVibe || '').length > this.vibeMax) {
      this.weeklyVibe = (this.weeklyVibe || '').slice(0, this.vibeMax);
    }
  }

  back() {
    this.error = '';
    if (this.step === 1) {
      window.history.back();
      return;
    }
    this.step = (this.step - 1) as any;
  }

  next() {
    this.error = '';
    if (this.step === 1 && !this.bioValid) {
      this.error = 'Bio is required (max 200 characters).';
      return;
    }
    if (this.step < 3) this.step = (this.step + 1) as any;
  }

  // Skip Step 2: clear preference fields only
  skipStep2() {
    this.prefEthnicity = '';
    this.prefReligion = '';
    this.prefHeight = '';
    this.prefWorkout = '';
    this.prefSmoking = '';
    this.prefDrinking = '';
    this.prefWork = '';
    this.next();
  }

  // Skip Step 3: clear details + vibe, then save bio + preferences only
  skipStep3() {
    this.children = '';
    this.zodiac = '';
    this.diet = '';
    this.pets = '';
    this.hometown = '';
    this.hobbies = '';
    this.languages = [];
    this.weeklyVibe = '';
    this.save(true);
  }

  private buildOptionalFields(): OptionalFieldDto[] {
    const fields: OptionalFieldDto[] = [];

    const add = (key: string, value: string, visibility: VisibilityLevel) => {
      const v = (value || '').trim();
      if (!v) return;
      fields.push({ key, value: v, visibility });
    };

    // ✅ Preferences (matching-only) => pref_* keys (backend enforces MatchingOnly)
    add('pref_ethnicity', this.prefEthnicity, 'MatchingOnly');
    add('pref_religion', this.prefReligion, 'MatchingOnly');
    add('pref_height', this.prefHeight, 'MatchingOnly');
    add('pref_workout', this.prefWorkout, 'MatchingOnly');
    add('pref_smoking', this.prefSmoking, 'MatchingOnly');
    add('pref_drinking', this.prefDrinking, 'MatchingOnly');
    add('pref_work', this.prefWork, 'MatchingOnly');

    // ✅ Your details (visibility controlled)
    add('children', this.children, this.detailVis.children);
    add('zodiac', this.zodiac, this.detailVis.zodiac);
    add('diet', this.diet, this.detailVis.diet);
    add('pets', this.pets, this.detailVis.pets);
    add('hometown', this.hometown, this.detailVis.hometown);
    add('hobbies', this.hobbies, this.detailVis.hobbies);

    if (this.languages.length > 0) {
      add('languages', this.languages.join(', '), this.detailVis.languages);
    }

    return fields;
  }

  async save(skipVibe: boolean) {
    this.error = '';

    if (!this.bioValid) {
      this.error = 'Bio is required (max 200 characters).';
      this.step = 1;
      return;
    }

    const payload: any = {
      bio: this.bio.trim(),
      optionalFields: this.buildOptionalFields(),
      weeklyVibe: skipVibe ? '' : (this.weeklyVibe || '').trim(),
    };

    this.saving = true;
    try {
      await firstValueFrom(this.onboarding.saveDetails(payload));
      await this.router.navigateByUrl('/onboarding/review');
    } catch (e: any) {
      this.error = e?.error?.error || e?.message || 'Couldn’t save details. Please try again.';
    } finally {
      this.saving = false;
    }
  }
}
