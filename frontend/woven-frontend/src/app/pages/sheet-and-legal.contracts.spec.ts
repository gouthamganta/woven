import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { LegalComponent } from './legal/legal.component';
import { PulseSheetComponent } from './home/pulse-sheet.component';
import { HowItWorksSheetComponent } from './home/how-it-works-sheet.component';
import { PulseState } from '../services/pulse.service';

describe('Pulse sheet and legal content contracts', () => {
  for (const [url, heading] of [['/privacy', 'Privacy Policy'], ['/terms', 'Terms of Service'], ['/data-policy', 'Data Policy']]) {
    it(`renders the requested ${heading} tab and its navigation links`, async () => {
      TestBed.configureTestingModule({ imports: [LegalComponent], providers: [provideRouter([])] });
      const router = TestBed.inject(Router); vi.spyOn(router, 'url', 'get').mockReturnValue(url);
      const fixture = TestBed.createComponent(LegalComponent); fixture.detectChanges(); await fixture.whenStable();
      expect(fixture.nativeElement.querySelector('h1')?.textContent).toContain(heading); expect(fixture.nativeElement.querySelectorAll('nav a')).toHaveLength(3); expect(fixture.nativeElement.querySelector('nav a.active')?.getAttribute('href')).toBe(url);
    });
  }
  it('renders the how-it-works content and closes through the actual overlay', () => {
    TestBed.configureTestingModule({ imports: [HowItWorksSheetComponent] }); const fixture = TestBed.createComponent(HowItWorksSheetComponent); const close = vi.fn(); fixture.componentInstance.closed.subscribe(close); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('WOVEN'); fixture.nativeElement.querySelector('.overlay').click(); expect(close).toHaveBeenCalledTimes(1);
  });
  it('requires all three pulse picks, preserves initial answers and emits a completed draft', () => {
    const page = new PulseSheetComponent(); const saved = vi.fn(), closed = vi.fn(), skipped = vi.fn(); page.saved.subscribe(saved); page.closed.subscribe(closed); page.skipped.subscribe(skipped);
    page.ngOnChanges(); expect(page.complete).toBe(false); expect(page.cycleText).toContain('48 hours'); page.save(); expect(saved).not.toHaveBeenCalled();
    page.pick('d1_battery', 'high'); page.pick('d2_tone', 'playful'); page.pick('d3_role', 'driver'); expect(page.complete).toBe(true); expect(page.isSelected('d1_battery', 'high')).toBe(true); expect(page.isSelected('d2_tone', 'playful')).toBe(true); expect(page.isSelected('d3_role', 'driver')).toBe(true); page.save(); expect(saved).toHaveBeenCalledWith({ d1_battery: 'high', d2_tone: 'playful', d3_role: 'driver' }); page.close(); page.skip(); expect(closed).toHaveBeenCalledTimes(1); expect(skipped).toHaveBeenCalledTimes(1);
    page.state = { answers: { d1_battery: 'low' }, cycleEndUtc: '2026-10-10T00:00:00Z' } as PulseState; page.ngOnChanges(); expect(page.draft).toEqual({ d1_battery: 'low' }); expect(page.cycleText).toContain('resets'); page.readonly = true; page.pick('d1_battery', 'high'); expect(page.draft.d1_battery).toBe('low');
  });
  for (const [id, key, grade] of [['d1_battery', 'high', 'g-dark'], ['d1_battery', 'medium', 'g-mid'], ['d1_battery', 'low', 'g-light'], ['d2_tone', 'playful', 'g-dark'], ['d2_tone', 'serious', 'g-mid'], ['d2_tone', 'calm', 'g-light'], ['d3_role', 'driver', 'g-dark'], ['d3_role', 'copilot', 'g-mid'], ['d3_role', 'passenger', 'g-light']] as const) {
    it(`maps the ${id}/${key} visual grade without changing selection`, () => {
      const page = new PulseSheetComponent(); expect(page.gradeFor(id, key)).toBe(grade); expect(page.draft).toEqual({});
    });
  }
});
