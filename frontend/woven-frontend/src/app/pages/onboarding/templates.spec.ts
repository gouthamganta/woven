import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { BasicsOnboardingComponent } from './basics';
import { DetailsOnboardingComponent } from './details';
import { IntentOnboardingComponent } from './intent';
import { LifestyleOnboardingComponent } from './lifestyle';
import { ReviewOnboardingComponent } from './review';
import { WelcomeOnboardingComponent } from './welcome';

describe('Rendered onboarding controls', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  beforeEach(() => {
    api = Object.fromEntries(['submitWelcome', 'submitBasics', 'submitIntent', 'saveDetails', 'completeOnboarding']
      .map(name => [name, vi.fn().mockReturnValue(of({}))]));
    api['getReview'] = vi.fn().mockReturnValue(of({
      basics: { fullName: 'Synthetic Adult', gender: 'nonbinary', city: 'Synthetic City', distanceMiles: 25, interestedIn: ['everyone'] },
      intent: { primaryIntent: 'friendship', reflectionSentence: 'Synthetic reflection' },
      photos: [{ url: '/synthetic-photo', sortOrder: 0 }], bio: 'Synthetic bio',
    }));
  });

  async function render<T>(type: Type<T>) {
    TestBed.configureTestingModule({ imports: [type], providers: [provideRouter([]), { provide: OnboardingService, useValue: api }] });
    const fixture = TestBed.createComponent(type);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    return fixture;
  }

  for (const [type, heading] of [
    [BasicsOnboardingComponent, 'The basics.'], [DetailsOnboardingComponent, 'Tell us about you.'],
    [IntentOnboardingComponent, 'What are you here for?'], [LifestyleOnboardingComponent, 'Your lifestyle.'],
    [ReviewOnboardingComponent, 'This is you.'], [WelcomeOnboardingComponent, 'Welcome to Woven.'],
  ] as Array<[Type<unknown>, string]>) {
    it(`renders an accessible primary heading and real controls for ${type.name}`, async () => {
      const fixture = await render(type);
      const element: HTMLElement = fixture.nativeElement;
      expect(element.querySelector('h1')?.textContent?.trim()).toBe(heading);
      expect(element.querySelectorAll('button').length).toBeGreaterThan(0);
      expect(element.textContent).not.toContain('undefined');
    });
  }

  it('keeps the basics Continue control disabled until adult identity and location are supplied', async () => {
    const fixture = await render(BasicsOnboardingComponent);
    const element: HTMLElement = fixture.nativeElement;
    const button = element.querySelector<HTMLButtonElement>('.cta')!;
    expect(button.disabled).toBe(true);
    Object.assign(fixture.componentInstance, { firstName: 'Synthetic', dob: '2000-01-01', gender: 'nonbinary', cityText: 'Hyderabad' });
    fixture.componentInstance.interestedInSet.add('everyone');
    fixture.componentInstance.citySelectionChanged(); fixture.detectChanges(); await fixture.whenStable();
    expect(button.disabled).toBe(false);
    expect(element.textContent).toContain('years old');
  });

  it('displays a save error and re-enables intent editing after the actual Continue click fails', async () => {
    const fixture = await render(IntentOnboardingComponent);
    const page = fixture.componentInstance;
    page.primaryIntent = 'friendship'; page.reflection = 'Synthetic reflection'; page.mark();
    fixture.detectChanges(); await fixture.whenStable();
    api['submitIntent'].mockReturnValue(throwError(() => new Error('Synthetic outage')));
    const button = fixture.nativeElement.querySelector('.cta') as HTMLButtonElement;
    button.click(); await fixture.whenStable(); fixture.detectChanges();
    expect(api['submitIntent']).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('Could not save');
    expect(button.disabled).toBe(false);
  });

  it('shows the adult name, photo and intent in the actual review template', async () => {
    const fixture = await render(ReviewOnboardingComponent);
    expect(fixture.nativeElement.textContent).toContain('Synthetic Adult');
    expect(fixture.nativeElement.textContent).toContain('Synthetic reflection');
    expect(fixture.nativeElement.querySelector('img')?.getAttribute('src')).toBe('/synthetic-photo');
  });
});
