import { Component, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

const mk = (k: string, v: string) => ({ key: k, label: v });

const HOROSCOPE = [
  'Aries','Taurus','Gemini','Cancer','Leo','Virgo',
  'Libra','Scorpio','Sagittarius','Capricorn','Aquarius','Pisces',
].map(v => mk(v.toLowerCase(), v));

const EDUCATION = [
  'High school','Some college','Associate degree','Bachelor\'s degree',
  'Master\'s degree','Doctorate','Trade / Vocational','Prefer not to say',
].map(v => mk(v.toLowerCase().replace(/[^a-z]/g, '_'), v));

@Component({
  selector: 'woven-onboarding-details',
  standalone: true,
  imports: [CommonModule, FormsModule, OnboardingShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <woven-onboarding-shell
      title="Tell us about you."
      subtitle="This helps us write better introductions and find stronger matches."
      [stepNumber]="6"
      [totalSteps]="9"
      stepLabel="About You"
    >
      <div class="stack">

        <!-- Bio -->
        <div class="field">
          <label class="label">Bio <span class="opt">optional</span></label>
          <textarea
            class="textarea"
            [(ngModel)]="bio"
            placeholder="A line or two about what makes you, you."
            maxlength="300"
            rows="4"
          ></textarea>
          <div class="charCount" [class.warn]="bio.length > 260">{{ bio.length }} / 300</div>
        </div>

        <div class="divider"></div>

        <!-- Life basics -->
        <div class="section">
          <div class="sectionHead">Life</div>

          <div class="grid2">
            <div class="field">
              <label class="label">Job title</label>
              <input class="input" type="text" [(ngModel)]="jobTitle" placeholder="What do you do?" maxlength="80"/>
            </div>
            <div class="field">
              <label class="label">Hometown</label>
              <input class="input" type="text" [(ngModel)]="hometown" placeholder="Where are you from?" maxlength="80"/>
            </div>
          </div>

          <div class="field">
            <label class="label">Education</label>
            <div class="pills">
              <button
                *ngFor="let e of education"
                class="pill"
                [class.active]="educationLevel === e.key"
                (click)="educationLevel = e.key; mark()"
              >{{ e.label }}</button>
            </div>
          </div>

          <div class="grid2">
            <div class="field">
              <label class="label">School / University</label>
              <input class="input" type="text" [(ngModel)]="school" placeholder="Where did you study?" maxlength="100"/>
            </div>
            <div class="field">
              <label class="label">Height <span class="opt">optional</span></label>
              <input class="input" type="text" [(ngModel)]="height" placeholder="e.g. 178 cm" maxlength="20"/>
            </div>
          </div>

          <div class="field">
            <label class="label">Zodiac sign <span class="opt">optional</span></label>
            <div class="pills">
              <button
                *ngFor="let z of horoscopes"
                class="pill"
                [class.active]="horoscope === z.key"
                (click)="horoscope = z.key; mark()"
              >{{ z.label }}</button>
            </div>
          </div>
        </div>

        <p class="err" *ngIf="err">{{ err }}</p>

        <button class="cta" (click)="next()" [disabled]="loading">
          <span *ngIf="!loading">Continue →</span>
          <span *ngIf="loading">Saving…</span>
        </button>

        <button class="skip" (click)="skip()">Skip for now</button>

      </div>
    </woven-onboarding-shell>
  `,
  styles: [`
    .stack { display: grid; gap: 20px; }
    .divider { height: 1px; background: linear-gradient(90deg, transparent, var(--border-soft), transparent); }
    .section { display: grid; gap: 16px; }
    .sectionHead { font-family: var(--font-display); font-size: 16px; font-weight: 400; color: var(--text-secondary); letter-spacing: -0.01em; }
    .grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .field { display: grid; gap: 8px; }
    .label { font-family: var(--font-ui); font-size: 11px; font-weight: 700; letter-spacing: 0.12em; text-transform: uppercase; color: var(--text-muted); }
    .opt { font-weight: 500; opacity: 0.7; text-transform: none; letter-spacing: 0; }

    .input {
      width: 100%; padding: 12px 14px; background: rgba(255,255,255,0.04);
      border: 1px solid var(--border-subtle); border-radius: var(--radius-lg);
      color: var(--text-primary); font-family: var(--font-ui); font-size: 14px;
      outline: none; transition: border-color 0.2s ease; box-sizing: border-box;
    }
    .input::placeholder { color: var(--text-dim); }
    .input:focus { border-color: var(--gold-400); }

    .textarea {
      width: 100%; padding: 12px 14px; background: rgba(255,255,255,0.04);
      border: 1px solid var(--border-subtle); border-radius: var(--radius-lg);
      color: var(--text-primary); font-family: var(--font-ui); font-size: 14px;
      line-height: 1.6; resize: vertical; outline: none; transition: border-color 0.2s ease; box-sizing: border-box;
    }
    .textarea::placeholder { color: var(--text-dim); }
    .textarea:focus { border-color: var(--gold-400); }

    .charCount { font-family: var(--font-data); font-size: 11px; color: var(--text-dim); text-align: right; transition: color 0.2s; }
    .charCount.warn { color: var(--rose-300); }

    .pills { display: flex; flex-wrap: wrap; gap: 7px; }
    .pill {
      padding: 8px 14px; border: 1px solid var(--border-subtle); border-radius: 9999px;
      background: transparent; color: var(--text-muted); font-family: var(--font-ui);
      font-size: 12px; font-weight: 500; cursor: pointer; transition: all 0.15s ease;
    }
    .pill:hover { border-color: var(--border-soft); color: var(--text-secondary); }
    .pill.active { border-color: var(--gold-400); background: rgba(212,160,23,0.10); color: var(--gold-300); font-weight: 600; }

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
export class DetailsOnboardingComponent {
  education  = EDUCATION;
  horoscopes = HOROSCOPE;

  bio            = '';
  jobTitle       = '';
  hometown       = '';
  school         = '';
  height         = '';
  educationLevel = '';
  horoscope      = '';

  loading = false;
  err = '';

  constructor(
    private onboarding: OnboardingService,
    private router: Router,
    private cdr: ChangeDetectorRef,
  ) {}

  mark() { this.cdr.markForCheck(); }

  buildFields() {
    const f: { key: string; value: string; visibility: string }[] = [];
    if (this.jobTitle)       f.push({ key: 'job',        value: this.jobTitle,       visibility: 'Public' });
    if (this.hometown)       f.push({ key: 'hometown',   value: this.hometown,       visibility: 'Public' });
    if (this.school)         f.push({ key: 'school',     value: this.school,         visibility: 'Public' });
    if (this.educationLevel) f.push({ key: 'education',  value: this.educationLevel, visibility: 'Public' });
    if (this.height)         f.push({ key: 'pref_height',value: this.height,         visibility: 'MatchingOnly' });
    if (this.horoscope)      f.push({ key: 'horoscope',  value: this.horoscope,      visibility: 'Public' });
    return f;
  }

  async next() {
    this.loading = true; this.err = ''; this.cdr.markForCheck();
    try {
      await firstValueFrom(this.onboarding.saveDetails({
        bio: this.bio.trim(),
        optionalFields: this.buildFields(),
      }));
      this.router.navigateByUrl('/onboarding/lifestyle');
    } catch {
      this.err = 'Could not save. Please try again.';
    } finally {
      this.loading = false; this.cdr.markForCheck();
    }
  }

  async skip() {
    this.bio = '';
    await this.next();
  }
}
