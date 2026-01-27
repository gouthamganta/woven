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
    .error{
      margin: 8px 0 12px;
      padding: 10px 12px;
      border-radius: 12px;
      background: rgba(255, 60, 60, 0.10);
      border: 1px solid rgba(255, 60, 60, 0.25);
      color: #7a0b0b;
      font-size: 13px;
    }

    .notice{
      margin: 8px 0 12px;
      padding: 10px 12px;
      border-radius: 12px;
      border: 1px solid rgba(0,0,0,.12);
      background: rgba(0,0,0,.03);
      font-size: 13px;
      opacity: .9;
      line-height: 1.35;
    }

    .block{ margin: 12px 0; }
    .label{ font-weight: 800; font-size: 13px; display:block; margin-bottom: 8px; }
    .req{ color: #b00020; }

    .input, .textarea{
      width: 100%;
      border-radius: 12px;
      border: 1px solid rgba(0,0,0,.12);
      background: rgba(0,0,0,.02);
      padding: 10px 12px;
      outline: none;
      font-size: 14px;
    }

    .textarea{ resize: vertical; min-height: 96px; }

    .row{
      display:flex;
      justify-content: space-between;
      gap: 12px;
      margin-top: 6px;
      align-items: center;
    }

    .mini{ font-size: 12px; opacity: .65; }
    .count{ font-size: 12px; opacity: .65; font-weight: 800; }

    .helper{ margin-top: 8px; font-size: 12px; opacity: .7; line-height: 1.35; }

    .grid{
      display:grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    @media (max-width: 560px){
      .grid{ grid-template-columns: 1fr; }
    }

    .actions{
      margin-top: 16px;
      display:flex;
      justify-content: space-between;
      gap: 10px;
      align-items:center;
      flex-wrap: wrap;
    }

    .ghost, .primary{
      border-radius: 12px;
      border: 1px solid rgba(0,0,0,.12);
      padding: 10px 12px;
      font-weight: 800;
      cursor: pointer;
      background: transparent;
    }

    .primary{
      background: #111;
      color: #fff;
      border-color: #111;
    }

    button:disabled{
      opacity: .55;
      cursor: not-allowed;
    }

    .chips{
      display:flex;
      gap: 8px;
      flex-wrap: wrap;
      margin-top: 6px;
    }

    .chip{
      border-radius: 999px;
      border: 1px solid rgba(0,0,0,.12);
      background: rgba(0,0,0,.02);
      padding: 8px 10px;
      font-weight: 800;
      font-size: 12px;
      cursor: pointer;
    }

    .chip.active{
      background: #111;
      color: #fff;
      border-color: #111;
    }

    .miniBtn{
      margin-top: 8px;
      border-radius: 10px;
      border: 1px solid rgba(0,0,0,.12);
      padding: 8px 10px;
      font-weight: 800;
      background: rgba(0,0,0,.02);
      cursor: pointer;
      font-size: 12px;
    }

    .visRow{
      display:flex;
      justify-content: space-between;
      align-items: center;
      gap: 10px;
      margin-top: 10px;
    }

    .seg{
      display:flex;
      gap: 6px;
      flex-wrap: wrap;
      justify-content: flex-end;
    }

    .segBtn{
      border-radius: 999px;
      border: 1px solid rgba(0,0,0,.12);
      background: rgba(0,0,0,.02);
      padding: 6px 10px;
      font-weight: 900;
      font-size: 12px;
      cursor: pointer;
      opacity: .9;
    }

    .segBtn.on{
      background: #111;
      color: #fff;
      border-color: #111;
      opacity: 1;
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
