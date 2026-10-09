import { ChangeDetectorRef } from '@angular/core';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { BasicsOnboardingComponent } from './basics';
import { IntentOnboardingComponent } from './intent';
import { DetailsOnboardingComponent } from './details';
import { LifestyleOnboardingComponent } from './lifestyle';
import { ReviewOnboardingComponent } from './review';
import { WelcomeOnboardingComponent } from './welcome';

describe('Onboarding validation and save recovery', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let router: { navigateByUrl: ReturnType<typeof vi.fn> };
  let cdr: ChangeDetectorRef;
  beforeEach(() => {
    api = Object.fromEntries(['submitWelcome', 'submitBasics', 'submitIntent', 'saveDetails', 'getReview', 'completeOnboarding']
      .map(name => [name, vi.fn().mockReturnValue(of({}))]));
    router = { navigateByUrl: vi.fn().mockResolvedValue(true) };
    cdr = { markForCheck: vi.fn() } as unknown as ChangeDetectorRef;
    vi.useFakeTimers(); vi.setSystemTime(new Date('2026-10-09T12:00:00Z'));
  });
  afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); });
  const service = () => api as unknown as OnboardingService;
  const navigation = () => router as unknown as Router;
  function validBasics() {
    const page = new BasicsOnboardingComponent(service(), navigation(), cdr);
    page.firstName = ' Synthetic '; page.dob = '2000-01-01'; page.gender = 'nonbinary';
    page.interestedInSet.add('everyone'); page.cityText = 'Hyderabad'; page.citySelectionChanged();
    return page;
  }

  it('does not submit incomplete basics or an underage profile', async () => {
    const page = new BasicsOnboardingComponent(service(), navigation(), cdr);
    expect(page.age).toBeNull(); await page.next();
    const underage = validBasics(); underage.dob = '2015-01-01';
    expect(underage.canProceed).toBe(false); await underage.next();
    expect(api['submitBasics']).not.toHaveBeenCalled();
  });

  it('builds and clears a date when dropdown selection changes', () => {
    const page = validBasics(); page.dobYear = '2000'; page.dobMonth = '02'; page.dobDay = '3'; page.updateDob();
    expect(page.dob).toBe('2000-02-03'); page.dobDay = ''; page.updateDob(); expect(page.dob).toBe('');
  });

  it('toggles selections and invalidates an unsupported city', () => {
    const page = validBasics(); page.toggleSet(page.orientationSet, 'queer'); expect(page.orientationSet.has('queer')).toBe(true);
    page.toggleSet(page.orientationSet, 'queer'); expect(page.orientationSet.size).toBe(0);
    page.cityText = 'Unknown'; page.citySelectionChanged(); expect(page.selectedCity).toBeNull(); expect(page.canProceed).toBe(false);
    page.mark();
  });

  it('keeps preferred age bounds adult, ordered, and at most 80', () => {
    const page = validBasics(); page.ageMin = 18; page.ageMax = 80;
    page.adjustAge('min', -1); page.adjustAge('max', 1); expect([page.ageMin, page.ageMax]).toEqual([18, 80]);
    page.adjustAge('min', 1); page.adjustAge('max', -1); expect([page.ageMin, page.ageMax]).toEqual([19, 79]);
    page.adjustAge('min', 60); page.adjustAge('max', -60); expect([page.ageMin, page.ageMax]).toEqual([19, 79]);
  });

  for (const enriched of [false, true]) {
    it(`submits valid basics with ${enriched ? 'selected' : 'omitted'} optional identity fields`, async () => {
      const page = validBasics();
      if (enriched) { page.pronouns_sel = 'they_them'; page.orientationSet.add('queer'); page.lookingForSet.add('long_term'); }
      await page.next();
      expect(api['submitBasics'].mock.calls[0][0]).toMatchObject({ fullName: 'Synthetic', city: 'Hyderabad', gender: 'nonbinary', interestedIn: ['everyone'] });
      expect(api['submitBasics'].mock.calls[0][0].pronouns).toBe(enriched ? 'they_them' : undefined);
      expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/photos'); expect(page.loading).toBe(false);
    });
  }

  it('keeps basics editable after a rejected save and accepts the backend next route', async () => {
    const page = validBasics(); api['submitBasics'].mockReturnValueOnce(throwError(() => new Error('Synthetic outage')));
    await page.next(); expect(page.err).not.toBe(''); expect(page.loading).toBe(false); expect(router.navigateByUrl).not.toHaveBeenCalled();
    api['submitBasics'].mockReturnValue(of({ nextRoute: '/onboarding/intent' })); await page.next();
    expect(page.err).toBe(''); expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/intent');
  });

  it('requires intent and a nonblank reflection, and supports deselection', async () => {
    const page = new IntentOnboardingComponent(service(), navigation(), cdr);
    await page.next(); page.primaryIntent = 'long_term'; page.reflection = '   '; await page.next();
    expect(api['submitIntent']).not.toHaveBeenCalled();
    page.toggleOpenness('friendship'); page.toggleOpenness('friendship'); expect(page.opennessSet.size).toBe(0); page.mark();
  });

  it('saves a trimmed intent reflection, recovers failure and follows the server route', async () => {
    const page = new IntentOnboardingComponent(service(), navigation(), cdr); page.primaryIntent = 'long_term'; page.reflection = ' Synthetic reflection ';
    page.toggleOpenness('friendship'); api['submitIntent'].mockReturnValueOnce(throwError(() => new Error('Synthetic outage')));
    await page.next(); expect(page.err).not.toBe(''); expect(page.loading).toBe(false);
    await page.next(); expect(api['submitIntent']).toHaveBeenLastCalledWith({ primaryIntent: 'long_term', openness: ['friendship'], reflectionSentence: 'Synthetic reflection' });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/foundational');
    api['submitIntent'].mockReturnValue(of({ nextRoute: '/onboarding/review' })); await page.next(); expect(router.navigateByUrl).toHaveBeenLastCalledWith('/onboarding/review');
  });

  it('omits empty details and keeps height matching-only', async () => {
    const page = new DetailsOnboardingComponent(service(), navigation(), cdr); expect(page.buildFields()).toEqual([]);
    Object.assign(page, { bio: ' Synthetic bio ', jobTitle: 'Engineer', hometown: 'Town', school: 'School', educationLevel: 'college', height: '170', horoscope: 'leo' });
    const fields = page.buildFields(); expect(fields).toHaveLength(6); expect(fields.find(f => f.key === 'pref_height')?.visibility).toBe('MatchingOnly');
    expect(fields.filter(f => f.key !== 'pref_height').every(f => f.visibility === 'Public')).toBe(true);
    page.mark(); await page.next(); expect(api['saveDetails'].mock.calls[0][0].bio).toBe('Synthetic bio'); expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/lifestyle');
  });

  it('does not navigate after details failure and allows skipping the bio', async () => {
    const page = new DetailsOnboardingComponent(service(), navigation(), cdr); page.bio = 'Synthetic';
    api['saveDetails'].mockReturnValueOnce(throwError(() => new Error('Synthetic outage'))); await page.next();
    expect(page.err).not.toBe(''); expect(page.loading).toBe(false); expect(router.navigateByUrl).not.toHaveBeenCalled();
    await page.skip(); expect(api['saveDetails'].mock.calls[1][0].bio).toBe('');
  });

  it('caps hobbies at ten while allowing removal and replacement', () => {
    const page = new LifestyleOnboardingComponent(service(), navigation(), cdr);
    for (let i = 0; i < 11; i++) page.toggleHobby(`hobby${i}`);
    expect(page.hobbies.size).toBe(10); expect(page.hobbies.has('hobby10')).toBe(false);
    page.toggleHobby('hobby0'); page.toggleHobby('hobby10'); expect(page.hobbies.size).toBe(10); expect(page.hobbies.has('hobby10')).toBe(true);
    page.toggle(page.petsSet, 'cat'); page.toggle(page.petsSet, 'cat'); expect(page.petsSet.size).toBe(0); page.mark();
  });

  it('preserves matching-only lifestyle visibility when saving selected fields', async () => {
    const page = new LifestyleOnboardingComponent(service(), navigation(), cdr); expect(page.buildFields()).toEqual([]);
    Object.assign(page, { children: 'no', drinkingVal: 'sometimes', smokingVal: 'never', exerciseVal: 'weekly', mbti: 'INTJ' });
    for (const set of [page.petsSet, page.dietSet, page.loveLangSet, page.languagesSet, page.hobbies]) set.add('synthetic');
    const fields = page.buildFields(); expect(fields).toHaveLength(10);
    expect(fields.filter(f => ['pref_drinking', 'pref_smoking'].includes(f.key)).every(f => f.visibility === 'MatchingOnly')).toBe(true);
    await page.next(); expect(api['saveDetails']).toHaveBeenCalledWith({ optionalFields: fields }); expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/review');
  });

  it('recovers lifestyle save failures and lets a user skip without making a save request', async () => {
    const page = new LifestyleOnboardingComponent(service(), navigation(), cdr); api['saveDetails'].mockReturnValue(throwError(() => new Error('Synthetic outage')));
    await page.next(); expect(page.err).not.toBe(''); expect(page.loading).toBe(false);
    api['saveDetails'].mockClear(); await page.skip(); expect(api['saveDetails']).not.toHaveBeenCalled(); expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/review');
  });

  it('supports review response shapes without exposing absent sections', async () => {
    const page = new ReviewOnboardingComponent(service(), navigation(), cdr);
    expect(page.basics).toBeNull(); expect(page.intent).toBeNull(); expect(page.photos).toEqual([]); expect(page.bio).toBe('');
    const flat = { basics: { fullName: 'Synthetic' }, intent: { primaryIntent: 'friendship' }, photos: [{ url: '/synthetic' }], bio: 'Synthetic bio' };
    api['getReview'].mockReturnValue(of(flat)); await page.ngOnInit(); expect(page.basics).toEqual(flat.basics); expect(page.photos).toEqual(flat.photos); expect(page.bio).toBe(flat.bio); expect(page.intent).toEqual(flat.intent);
    api['getReview'].mockReturnValue(of({ self: flat })); await page.ngOnInit(); expect(page.basics).toEqual(flat.basics); expect(page.photos).toEqual(flat.photos); expect(page.bio).toBe(flat.bio); expect(page.intent).toEqual(flat.intent);
    api['getReview'].mockReturnValue(of({ fullName: 'Synthetic' })); await page.ngOnInit(); expect(page.basics).toEqual({ fullName: 'Synthetic' });
  });

  it('formats array, JSON and plain-text optional values safely', () => {
    const page = new ReviewOnboardingComponent(service(), navigation(), cdr);
    expect(page.formatList(['a', 'b'])).toBe('a, b'); expect(page.formatList('["a","b"]')).toBe('a, b'); expect(page.formatList('plain')).toBe('plain'); expect(page.formatList(null)).toBe('');
    page.editStep('/onboarding/photos'); expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/photos');
  });

  it('exposes review loading/confirmation failures and recovers on retry', async () => {
    const page = new ReviewOnboardingComponent(service(), navigation(), cdr); api['getReview'].mockReturnValue(throwError(() => new Error('Synthetic outage')));
    await page.ngOnInit(); expect(page.loading).toBe(false); expect(page.err).not.toBe('');
    api['completeOnboarding'].mockReturnValueOnce(throwError(() => new Error('Synthetic outage'))); await page.confirm(); expect(page.confirming).toBe(false); expect(router.navigateByUrl).not.toHaveBeenCalled();
    await page.confirm(); expect(page.err).toBe(''); expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/start');
    api['completeOnboarding'].mockReturnValue(of({ nextRoute: '/moments' })); await page.confirm(); expect(router.navigateByUrl).toHaveBeenLastCalledWith('/moments');
  });

  it('recovers welcome submission failure and honors server or fallback routes', async () => {
    const page = new WelcomeOnboardingComponent(service(), navigation(), cdr); api['submitWelcome'].mockReturnValueOnce(throwError(() => new Error('Synthetic outage')));
    await page.next(); expect(page.loading).toBe(false); expect(page.err).not.toBe(''); expect(router.navigateByUrl).not.toHaveBeenCalled();
    await page.next(); expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/basics');
    api['submitWelcome'].mockReturnValue(of({ nextRoute: '/onboarding/photos' })); await page.next(); expect(router.navigateByUrl).toHaveBeenLastCalledWith('/onboarding/photos');
  });
});
