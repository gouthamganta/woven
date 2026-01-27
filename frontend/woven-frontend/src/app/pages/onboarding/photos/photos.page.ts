import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from '../../../onboarding/onboarding.service';
import { OnboardingShellComponent } from '../onboarding-shell';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

type PhotoSlot = {
  dataUrl: string | null;
  caption: string;
};

@Component({
  selector: 'app-photos-page',
  standalone: true,
  imports: [CommonModule, FormsModule, OnboardingShellComponent],
  templateUrl: './photos.page.html',
  styleUrls: ['./photos.page.css'],
})
export class PhotosPageComponent {
  readonly minRequired = 3;
  readonly maxSlots = 6;
  readonly captionMax = 40;

  // Shell progress
  readonly totalSteps = 6;

  stepIndex = 0; // 0..5 photo steps, 6 summary
  slots: PhotoSlot[] = Array.from({ length: this.maxSlots }, () => ({ dataUrl: null, caption: '' }));

  saving = false;
  error = '';
  showWhy = false;

  readonly titles: string[] = [
    'Photo 1',
    'Photo 2',
    'Photo 3',
    'Photo 4 (Optional)',
    'Photo 5 (Optional)',
    'Photo 6 (Optional)',
  ];

  readonly hints: string[] = [
    "The 'Hello' shot. Clear eyes, easy smile, and great light. Let them see who they’re talking to.",
    "The full-body vibe. From your favorite sneakers to your go-to outfit—show how you show up.",
    "You in the zone. Show us what you’re like when you’re building, exploring, or just geeking out.",
    "Optional: Captured in the wild. A moment you didn’t plan, but totally sums you up. Friends' favorites go here.",
    "Optional: Give them a reason to ask. A travel memory, a project you finished, or a niche hobby that’s uniquely yours.",
    "Optional: The Wildcard. A little bit of mystery, a lot of personality. Make them stop their scroll.",
  ];

  constructor(private onboarding: OnboardingService, private router: Router) {}

  // ✅ always access by index (prevents “remove all” weirdness)
  slotAt(i: number): PhotoSlot {
    return this.slots[i];
  }

  get isSummary() {
    return this.stepIndex === this.maxSlots;
  }

  get isOptionalStep() {
    return this.stepIndex >= 3 && this.stepIndex <= 5;
  }

  // For shell
  get shellTitle() {
    return this.isSummary ? 'Review your photos' : this.titles[this.stepIndex];
  }

  get shellSubtitle() {
    return this.isSummary ? 'Quick check — you can always tweak these later.' : this.hints[this.stepIndex];
  }

  get stepNumberForShell() {
    // Summary should still show 6/6
    return this.isSummary ? 6 : this.stepIndex + 1;
  }

  get stepLabelForShell() {
    return this.isSummary ? 'Photos' : `Photos • ${this.stepIndex + 1}/${this.maxSlots}`;
  }

  get filledCount() {
    return this.slots.filter(s => !!s.dataUrl).length;
  }

  get progressText() {
    return this.isSummary
      ? `Done • ${this.filledCount}/${this.maxSlots} added`
      : `Step ${this.stepIndex + 1} of ${this.maxSlots}`;
  }

  get canProceed() {
    if (this.saving) return false;

    if (this.isSummary) return this.filledCount >= this.minRequired;

    // required steps 1..3
    if (this.stepIndex <= 2) return !!this.slotAt(this.stepIndex).dataUrl;

    // optional can proceed even if empty
    return true;
  }

  toggleWhy() {
    this.showWhy = !this.showWhy;
  }

  onFileChange(ev: Event) {
    this.error = '';
    const input = ev.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    const file = input.files[0];
    input.value = '';

    if (!file.type.startsWith('image/')) {
      this.error = 'That doesn’t look like an image. Try a photo instead 🙂';
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      const dataUrl = String(reader.result || '');
      if (!dataUrl) return;
      this.slotAt(this.stepIndex).dataUrl = dataUrl;
    };
    reader.onerror = () => (this.error = 'Couldn’t read that photo. Try a different one.');
    reader.readAsDataURL(file);
  }

  removeCurrent() {
    this.error = '';
    const slot = this.slotAt(this.stepIndex);
    slot.dataUrl = null;
    slot.caption = '';
  }

  goBack() {
    this.error = '';
    this.showWhy = false;

    if (this.stepIndex === 0) {
      window.history.back();
      return;
    }
    this.stepIndex = Math.max(0, this.stepIndex - 1);
  }

  // ✅ Skip optional should CLEAR and advance
  skipOptional() {
    if (!this.isOptionalStep) return;
    this.error = '';
    this.showWhy = false;

    const slot = this.slotAt(this.stepIndex);
    slot.dataUrl = null;
    slot.caption = '';

    this.stepIndex = Math.min(this.maxSlots, this.stepIndex + 1);
  }

  next() {
    this.error = '';
    this.showWhy = false;

    if (!this.isSummary && this.stepIndex <= 2 && !this.slotAt(this.stepIndex).dataUrl) {
      this.error = 'Add one photo here — then you’re good to go.';
      return;
    }

    if (!this.isSummary) {
      this.stepIndex = Math.min(this.maxSlots, this.stepIndex + 1);
    }
  }

  jumpTo(i: number) {
    if (i < 0 || i >= this.maxSlots) return;
    this.stepIndex = i;
  }

  async saveAndContinue() {
    this.error = '';

    if (this.filledCount < this.minRequired) {
      this.error = `Add at least ${this.minRequired} photos to continue.`;
      return;
    }

    // ✅ Backend expects: { photos: [{ url, caption, sortOrder }] }
    const payload = {
      photos: this.slots
        .map((s, i) => ({ s, i }))
        .filter(x => typeof x.s.dataUrl === 'string' && x.s.dataUrl.length > 0)
        .map(x => ({
          url: x.s.dataUrl as string, // ✅ url (not dataUrl)
          caption: (x.s.caption || '').slice(0, this.captionMax),
          sortOrder: x.i + 1,         // ✅ sortOrder (not order)
        })),
    };

    this.saving = true;
    try {
      await firstValueFrom(this.onboarding.savePhotos(payload));

      // ✅ Desired flow: photos -> intent
      await this.router.navigateByUrl('/onboarding/intent');
      // If you want to go to details instead, use:
      // await this.router.navigateByUrl('/onboarding/details');
    } catch (e: any) {
      this.error =
        e?.error?.error ||
        e?.error?.message ||
        e?.message ||
        'Couldn’t save photos. Please try again.';
    } finally {
      this.saving = false;
    }
  }
}

