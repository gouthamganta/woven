import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MatchesService, MatchProfileResponse } from '../../services/matches.service';

type PhotoView = { url: string; caption?: string | null; sortOrder: number };
type PublicOptionalField = { key: string; value: string };

@Component({
  selector: 'app-match-profile-preview',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './match-profile-preview.page.html',
  styleUrls: ['./match-profile-preview.page.scss'],
})
export class MatchProfilePreviewPageComponent implements OnInit {
  loading = true;
  error = '';
  toast = '';

  matchId = '';
  data: MatchProfileResponse | null = null;

  captionOpenIndex: number | null = null;
  captionText = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private matches: MatchesService
  ) {}

  async ngOnInit() {
    const id = this.route.snapshot.paramMap.get('matchId');
    if (!id) {
      this.error = 'Missing match id';
      this.loading = false;
      return;
    }
    this.matchId = id;
    await this.load();
  }

  async load() {
    this.loading = true;
    this.error = '';
    try {
      this.data = await firstValueFrom(this.matches.profile(this.matchId));
      this.captionOpenIndex = null;
      this.captionText = '';
    } catch (e: any) {
      this.error =
        e?.error?.error ||
        e?.error?.message ||
        e?.message ||
        'Could not load profile.';
    } finally {
      this.loading = false;
    }
  }

  back() {
    this.router.navigateByUrl('/home/chats');
  }

  toggleCaption(idx: number, caption?: string | null) {
    if (this.captionOpenIndex === idx) {
      this.captionOpenIndex = null;
      this.captionText = '';
      return;
    }
    this.captionOpenIndex = idx;
    const text = (caption || '').toString().trim();
    this.captionText = text.length ? text : 'No caption added.';
  }

  prettyKey(k: string): string {
    if (!k) return 'Field';
    const cleaned = k.replace(/^pref_/, '').replace(/_/g, ' ');
    return cleaned.replace(/\b\w/g, (c) => c.toUpperCase());
  }

  private showToast(msg: string) {
    this.toast = msg;
    window.setTimeout(() => (this.toast = ''), 1400);
  }

  // ===========================
  // VIEW MODEL (same vibe as Review public tab)
  // ===========================
  get publicName(): string {
    return (this.data?.publicPreview?.name || '').toString().trim() || '—';
  }

  get publicAge(): string {
    const age = this.data?.publicPreview?.age;
    return age != null ? String(age) : '';
  }

  get publicGender(): string {
    return (this.data?.publicPreview?.gender || '').toString().trim();
  }

  get publicLocation(): string {
    return (this.data?.publicPreview?.location || '').toString().trim();
  }

  get publicIntentPrimary(): string {
    return (this.data?.publicPreview?.intent?.primaryIntent || '').toString().trim();
  }

  get publicIntentOpennessLine(): string {
    const arr = this.data?.publicPreview?.intent?.openness;
    const list = Array.isArray(arr) ? arr.map((x) => String(x)) : [];
    return list.length ? `Open to: ${list.join(', ')}` : '';
  }

  get publicBio(): string {
    return (this.data?.publicPreview?.bio || '').toString().trim();
  }

  get publicOptional(): PublicOptionalField[] {
    const arr: any[] = Array.isArray(this.data?.publicPreview?.optionalPublic)
      ? (this.data as any).publicPreview.optionalPublic
      : [];
    return arr
      .map((x: any) => ({
        key: (x?.key || '').toString(),
        value: (x?.value || '').toString(),
      }))
      .filter((x) => x.key.trim().length && x.value.trim().length);
  }

  get publicPhotos(): PhotoView[] {
    const p: any[] = Array.isArray(this.data?.publicPreview?.photos)
      ? (this.data as any).publicPreview.photos
      : [];
    return p
      .map((x: any) => ({
        url: (x?.url || '').toString(),
        caption: x?.caption ?? '',
        sortOrder: Number(x?.sortOrder ?? 999),
      }))
      .filter((x) => x.url.trim().length)
      .sort((a, b) => a.sortOrder - b.sortOrder);
  }

  get basicsChips(): string[] {
    const chips: string[] = [];
    if (this.publicGender) chips.push(this.publicGender);
    if (this.publicLocation) chips.push(this.publicLocation);
    if (this.publicIntentPrimary) chips.push(this.publicIntentPrimary);
    return chips;
  }

  get accessHint(): string {
    const lvl = this.data?.accessLevel;
    if (!lvl) return '';
    if (lvl === 'FULL') return 'Full preview unlocked.';
    return 'Limited preview — unlocks after 2-way chat.';
  }
}
