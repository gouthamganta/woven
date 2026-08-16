import { Component, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

const mk = (k: string, v: string) => ({ key: k, label: v });

const CHILDREN   = [mk('have_them','I have children'), mk('want_them','I want children'), mk('dont_want','I don\'t want'), mk('open','Open to it'), mk('prefer_not','Prefer not to say')];
const PETS       = ['Dog','Cat','Other','None'].map(v => mk(v.toLowerCase(), v));
const DIET       = ['Omnivore','Vegetarian','Vegan','Halal','Kosher','Other'].map(v => mk(v.toLowerCase(), v));
const DRINKING   = [mk('never','Never'), mk('rarely','Rarely'), mk('socially','Socially'), mk('often','Often')];
const SMOKING    = [mk('never','Never'), mk('socially','Socially'), mk('yes','Yes'), mk('prefer_not','Prefer not to say')];
const EXERCISE   = [mk('never','Never'), mk('sometimes','Sometimes'), mk('often','Often'), mk('daily','Daily')];
const LOVE_LANGS = ['Words of affirmation','Physical touch','Acts of service','Gift giving','Quality time'].map(v => mk(v.toLowerCase().replace(/ /g,'_'), v));
const LANGUAGES  = ['English','Spanish','French','German','Mandarin','Hindi','Arabic','Portuguese','Japanese','Korean','Other'].map(v => mk(v.toLowerCase(), v));
const MBTI_TYPES = ['INTJ','INTP','ENTJ','ENTP','INFJ','INFP','ENFJ','ENFP','ISTJ','ISFJ','ESTJ','ESFJ','ISTP','ISFP','ESTP','ESFP'];
const HOBBIES_LIST = [
  'Reading','Writing','Gaming','Cooking','Baking','Hiking','Running','Yoga',
  'Gym','Cycling','Swimming','Dancing','Photography','Drawing','Painting',
  'Music','Travel','Film','Theatre','Stand-up comedy','Board games',
  'Rock climbing','Surfing','Skiing','Martial arts','Football','Basketball',
  'Tennis','Golf','Gardening','DIY','Fashion','Podcasts','Coding',
  'Volunteering','Meditation','Astronomy','Cars','Camping','Fishing',
];

@Component({
  selector: 'woven-onboarding-lifestyle',
  standalone: true,
  imports: [CommonModule, OnboardingShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <woven-onboarding-shell
      title="Your lifestyle."
      subtitle="All optional — but the more you share, the better your matches."
      [stepNumber]="7"
      [totalSteps]="9"
      stepLabel="Lifestyle"
    >
      <div class="stack">

        <p class="optNote">Pick what applies. You can always update these later.</p>

        <!-- Lifestyle -->
        <div class="section">
          <div class="sectionHead">Lifestyle</div>

          <div class="field">
            <label class="label">Children</label>
            <div class="pills">
              <button *ngFor="let c of childrenOpts" class="pill" [class.active]="children === c.key" (click)="children = c.key; mark()">{{ c.label }}</button>
            </div>
          </div>

          <div class="field">
            <label class="label">Pets</label>
            <div class="pills">
              <button *ngFor="let p of pets" class="pill" [class.active]="petsSet.has(p.key)" (click)="toggle(petsSet, p.key)">{{ p.label }}</button>
            </div>
          </div>

          <div class="field">
            <label class="label">Diet</label>
            <div class="pills">
              <button *ngFor="let d of diet" class="pill" [class.active]="dietSet.has(d.key)" (click)="toggle(dietSet, d.key)">{{ d.label }}</button>
            </div>
          </div>

          <div class="field">
            <label class="label">Drinking</label>
            <div class="pills">
              <button *ngFor="let d of drinking" class="pill" [class.active]="drinkingVal === d.key" (click)="drinkingVal = d.key; mark()">{{ d.label }}</button>
            </div>
          </div>

          <div class="field">
            <label class="label">Smoking</label>
            <div class="pills">
              <button *ngFor="let s of smoking" class="pill" [class.active]="smokingVal === s.key" (click)="smokingVal = s.key; mark()">{{ s.label }}</button>
            </div>
          </div>

          <div class="field">
            <label class="label">Exercise</label>
            <div class="pills">
              <button *ngFor="let e of exercise" class="pill" [class.active]="exerciseVal === e.key" (click)="exerciseVal = e.key; mark()">{{ e.label }}</button>
            </div>
          </div>
        </div>

        <div class="divider"></div>

        <!-- Personality -->
        <div class="section">
          <div class="sectionHead">Personality</div>

          <div class="field">
            <label class="label">Love language <span class="opt">pick all that apply</span></label>
            <div class="pills">
              <button *ngFor="let l of loveLangs" class="pill" [class.active]="loveLangSet.has(l.key)" (click)="toggle(loveLangSet, l.key)">{{ l.label }}</button>
            </div>
          </div>

          <div class="field">
            <label class="label">Languages spoken</label>
            <div class="pills">
              <button *ngFor="let l of languages" class="pill" [class.active]="languagesSet.has(l.key)" (click)="toggle(languagesSet, l.key)">{{ l.label }}</button>
            </div>
          </div>

          <div class="field">
            <label class="label">MBTI <span class="opt">optional</span></label>
            <div class="pills">
              <button *ngFor="let m of mbtiTypes" class="pill mbtiPill" [class.active]="mbti === m" (click)="mbti = mbti === m ? '' : m; mark()">{{ m }}</button>
            </div>
          </div>
        </div>

        <div class="divider"></div>

        <!-- Hobbies -->
        <div class="section">
          <div class="sectionHead">Hobbies & interests</div>
          <p class="hobbyHint">Pick up to 10 — these show on your profile.</p>
          <div class="pills">
            <button
              *ngFor="let h of hobbyList"
              class="pill"
              [class.active]="hobbies.has(h)"
              [disabled]="hobbies.size >= 10 && !hobbies.has(h)"
              (click)="toggleHobby(h)"
            >{{ h }}</button>
          </div>
          <div class="hobbyCount" *ngIf="hobbies.size > 0">{{ hobbies.size }} / 10 selected</div>
        </div>

        <p class="err" *ngIf="err">{{ err }}</p>

        <button class="cta" (click)="next()" [disabled]="loading">
          <span *ngIf="!loading">Continue →</span>
          <span *ngIf="loading">Saving…</span>
        </button>

        <button class="skip" (click)="skip()">Skip everything</button>

      </div>
    </woven-onboarding-shell>
  `,
  styles: [`
    .stack { display: grid; gap: 20px; }
    .divider { height: 1px; background: linear-gradient(90deg, transparent, var(--border-soft), transparent); }
    .section { display: grid; gap: 16px; }
    .sectionHead { font-family: var(--font-display); font-size: 16px; font-weight: 400; color: var(--text-secondary); letter-spacing: -0.01em; }
    .field { display: grid; gap: 8px; }
    .label { font-family: var(--font-ui); font-size: 11px; font-weight: 700; letter-spacing: 0.12em; text-transform: uppercase; color: var(--text-muted); }
    .opt { font-weight: 500; opacity: 0.7; text-transform: none; letter-spacing: 0; }

    .optNote {
      font-family: var(--font-ui); font-size: 12px; color: var(--text-dim); line-height: 1.55;
      padding: 12px 14px; background: rgba(212,160,23,0.05);
      border: 1px solid rgba(212,160,23,0.12); border-radius: var(--radius-lg); font-style: italic;
    }

    .hobbyHint { font-family: var(--font-ui); font-size: 12px; color: var(--text-dim); margin: -4px 0 0; }
    .hobbyCount { font-family: var(--font-data); font-size: 11px; color: var(--gold-300); font-weight: 600; }

    .pills { display: flex; flex-wrap: wrap; gap: 7px; }
    .pill {
      padding: 8px 14px; border: 1px solid var(--border-subtle); border-radius: 9999px;
      background: transparent; color: var(--text-muted); font-family: var(--font-ui);
      font-size: 12px; font-weight: 500; cursor: pointer; transition: all 0.15s ease;
    }
    .pill:hover:not(:disabled) { border-color: var(--border-soft); color: var(--text-secondary); }
    .pill.active { border-color: var(--gold-400); background: rgba(212,160,23,0.10); color: var(--gold-300); font-weight: 600; }
    .pill:disabled { opacity: 0.3; cursor: not-allowed; }

    .mbtiPill { padding: 7px 11px; font-size: 11px; font-family: var(--font-data); letter-spacing: 0.05em; }

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

    .skip {
      width: 100%; padding: 12px; border: 1px solid var(--border-subtle); border-radius: var(--radius-xl);
      background: transparent; color: var(--text-dim); font-family: var(--font-ui);
      font-size: 13px; cursor: pointer; transition: all 0.2s ease;
    }
    .skip:hover { color: var(--text-muted); border-color: var(--border-soft); }

    .err { font-size: 12px; color: var(--rose-300); }
  `],
})
export class LifestyleOnboardingComponent {
  childrenOpts = CHILDREN;
  pets         = PETS;
  diet         = DIET;
  drinking     = DRINKING;
  smoking      = SMOKING;
  exercise     = EXERCISE;
  loveLangs    = LOVE_LANGS;
  languages    = LANGUAGES;
  mbtiTypes    = MBTI_TYPES;
  hobbyList    = HOBBIES_LIST;

  children     = '';
  drinkingVal  = '';
  smokingVal   = '';
  exerciseVal  = '';
  mbti         = '';

  petsSet      = new Set<string>();
  dietSet      = new Set<string>();
  loveLangSet  = new Set<string>();
  languagesSet = new Set<string>();
  hobbies      = new Set<string>();

  loading = false;
  err = '';

  constructor(
    private onboarding: OnboardingService,
    private router: Router,
    private cdr: ChangeDetectorRef,
  ) {}

  mark() { this.cdr.markForCheck(); }

  toggle(set: Set<string>, key: string) {
    set.has(key) ? set.delete(key) : set.add(key);
    this.cdr.markForCheck();
  }

  toggleHobby(h: string) {
    if (this.hobbies.has(h)) { this.hobbies.delete(h); }
    else if (this.hobbies.size < 10) { this.hobbies.add(h); }
    this.cdr.markForCheck();
  }

  buildFields() {
    const f: { key: string; value: string; visibility: string }[] = [];
    const pub = 'Public'; const m = 'MatchingOnly';
    if (this.children)        f.push({ key: 'children',      value: this.children,                    visibility: pub });
    if (this.petsSet.size)    f.push({ key: 'pets',          value: [...this.petsSet].join(','),      visibility: pub });
    if (this.dietSet.size)    f.push({ key: 'diet',          value: [...this.dietSet].join(','),      visibility: pub });
    if (this.drinkingVal)     f.push({ key: 'pref_drinking', value: this.drinkingVal,                 visibility: m });
    if (this.smokingVal)      f.push({ key: 'pref_smoking',  value: this.smokingVal,                  visibility: m });
    if (this.exerciseVal)     f.push({ key: 'habits',        value: this.exerciseVal,                 visibility: pub });
    if (this.loveLangSet.size)f.push({ key: 'love_language', value: [...this.loveLangSet].join(','), visibility: pub });
    if (this.languagesSet.size)f.push({ key: 'languages',   value: [...this.languagesSet].join(','), visibility: pub });
    if (this.mbti)            f.push({ key: 'mbti',          value: this.mbti,                        visibility: pub });
    if (this.hobbies.size)    f.push({ key: 'hobbies',       value: [...this.hobbies].join(','),      visibility: pub });
    return f;
  }

  async next() {
    this.loading = true; this.err = ''; this.cdr.markForCheck();
    try {
      await firstValueFrom(this.onboarding.saveDetails({
        optionalFields: this.buildFields(),
      }));
      this.router.navigateByUrl('/onboarding/review');
    } catch {
      this.err = 'Could not save. Please try again.';
    } finally {
      this.loading = false; this.cdr.markForCheck();
    }
  }

  async skip() {
    this.router.navigateByUrl('/onboarding/review');
  }
}
