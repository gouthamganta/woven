import { Component, OnInit, ChangeDetectorRef, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { OnboardingShellComponent } from '../pages/onboarding/onboarding-shell';
import {
  OnboardingService,
  FoundationalQuestion,
  OnboardingStateResponse
} from './onboarding.service';

type AnswerRow = { questionId: string; answer: string };

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, OnboardingShellComponent],
  templateUrl: './foundational.component.html'
})
export class FoundationalComponent implements OnInit {
  // expose Math to template (if ever needed)
  Math = Math;

  loading = true;
  saving = false;
  error = '';

  // due-state fields
  version = 1;
  hardBlock = true;
  allowSkip = false;

  // questions + answers
  questions: FoundationalQuestion[] = [];
  answers: AnswerRow[] = [];

  // one-question flow
  currentIndex = 0;

  // gating
  minCharsPerAnswer = 30; // change to 20/40 if you want
  maxCharsPerAnswer = 400;

  helperPrompts = [
    'Keep it real — 1–3 sentences is perfect.',
    'Think “comfortable + safe”, not “impressive”.',
    'Small routines > big goals.',
    'Something personal that matters to you.',
    'Describe the everyday vibe (not a movie scene).'
  ];

  constructor(
    private onboarding: OnboardingService,
    private router: Router,
    private cdr: ChangeDetectorRef,
    private zone: NgZone
  ) {}

  async ngOnInit() {
    // SSR-safe: if server renders, don’t run network/token logic
    const isBrowser = typeof window !== 'undefined' && !!window.localStorage;
    if (!isBrowser) {
      this.loading = true;
      return;
    }

    console.log('🔄 Loading foundational questions...');

    try {
      // 1) Fetch state (to know allowSkip/hardBlock/version when due)
      const state: OnboardingStateResponse = await firstValueFrom(this.onboarding.getState());
      console.log('✅ State loaded:', state);

      if (state.profileStatus === 'FOUNDATIONAL_DUE') {
        this.version = state.version ?? 1;
        this.hardBlock = !!state.hardBlock;
        this.allowSkip = !!state.allowSkip;
      } else {
        // First onboarding = required
        this.allowSkip = false;
        this.hardBlock = true;
      }

      // 2) Fetch questions
      const qres = await firstValueFrom(this.onboarding.getFoundationalQuestions());
      console.log('✅ Questions loaded:', qres);

      this.version = qres.version ?? this.version;
      this.questions = qres.questions ?? [];

      // align answers with questions
      this.answers = this.questions.map(q => ({ questionId: q.id, answer: '' }));

      // show first question
      this.currentIndex = 0;

      // force UI refresh (SSR/hydration edge cases)
      this.zone.run(() => {
        this.loading = false;
        this.cdr.detectChanges();
      });

      console.log('✅ Loading complete');
    } catch (e) {
      console.error(e);
      this.error = 'Could not load foundational questions. Please refresh.';
      this.zone.run(() => {
        this.loading = false;
        this.cdr.detectChanges();
      });
    }
  }

  // helpers used by template
  hasQuestions(): boolean {
    return Array.isArray(this.questions) && this.questions.length === 5;
  }

  currentQuestion(): FoundationalQuestion | null {
    if (!this.hasQuestions()) return null;
    return this.questions[this.currentIndex] ?? null;
  }

  currentAnswer(): string {
    if (!this.answers?.length) return '';
    return this.answers[this.currentIndex]?.answer ?? '';
  }

  currentLen(): number {
    return (this.currentAnswer() ?? '').length;
  }

  remaining(): number {
    return this.maxCharsPerAnswer - this.currentLen();
  }

  canNext(): boolean {
    const a = (this.currentAnswer() ?? '').trim();
    return a.length >= this.minCharsPerAnswer && a.length <= this.maxCharsPerAnswer;
  }

  canSubmit(): boolean {
    if (!this.hasQuestions()) return false;
    if (!this.answers || this.answers.length !== 5) return false;
    return this.answers.every(x => (x.answer ?? '').trim().length >= this.minCharsPerAnswer);
  }

  back() {
    this.zone.run(() => {
      console.log('⬅️ Back clicked. Before:', this.currentIndex);
      if (this.currentIndex > 0) this.currentIndex--;
      console.log('⬅️ Back clicked. After:', this.currentIndex);
      this.cdr.detectChanges();
    });
  }

  next() {
    this.zone.run(() => {
      console.log(
        '➡️ Next clicked. Before:',
        this.currentIndex,
        'len:',
        this.currentLen(),
        'canNext:',
        this.canNext()
      );

      if (!this.canNext()) return;
      if (this.currentIndex < 4) this.currentIndex++;

      console.log('➡️ Next clicked. After:', this.currentIndex);
      this.cdr.detectChanges();
    });
  }

  private validate(): string | null {
    if (!this.hasQuestions()) return 'Question set is not ready. Reload.';
    if (!this.answers || this.answers.length !== 5) return 'Answers are not ready. Reload.';

    for (let i = 0; i < 5; i++) {
      const ans = (this.answers[i]?.answer ?? '').trim();
      if (ans.length < this.minCharsPerAnswer) return `Please write at least ${this.minCharsPerAnswer} characters for question ${i + 1}.`;
      if (ans.length > this.maxCharsPerAnswer) return `Answer ${i + 1} must be ${this.maxCharsPerAnswer} characters or less.`;
    }

    const ids = this.answers.map(a => a.questionId);
    const unique = new Set(ids);
    if (unique.size !== 5) return 'Duplicate questions detected. Reload the page.';

    return null;
  }

  async submit() {
    const err = this.validate();
    if (err) {
      this.error = err;
      this.cdr.detectChanges();
      return;
    }

    this.error = '';
    this.saving = true;
    this.cdr.detectChanges();

    try {
      const payload = {
        answers: this.answers.map(a => ({
          questionId: a.questionId,
          answer: (a.answer ?? '').trim()
        }))
      };

      const res = await firstValueFrom(this.onboarding.submitFoundationalAnswers(payload));
      const next = res?.nextRoute || '/home';
      this.router.navigateByUrl(next);
    } catch (e) {
      console.error(e);
      this.error = 'Could not save answers. Please try again.';
    } finally {
      this.saving = false;
      this.cdr.detectChanges();
    }
  }

  async defer() {
    if (!this.allowSkip) return;

    this.saving = true;
    this.error = '';
    this.cdr.detectChanges();

    try {
      const res = await firstValueFrom(this.onboarding.deferFoundational());
      this.router.navigateByUrl(res?.nextRoute || '/home');
    } catch (e) {
      console.error(e);
      this.error = 'Could not defer right now. Please try again.';
    } finally {
      this.saving = false;
      this.cdr.detectChanges();
    }
  }
}
