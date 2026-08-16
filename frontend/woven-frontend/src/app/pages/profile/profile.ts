import { Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { TilesService, MyTile } from '../../services/tiles.service';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">

      <div class="loading" *ngIf="loading">
        <div class="loadDot"></div><div class="loadDot"></div><div class="loadDot"></div>
      </div>

      <div class="errState" *ngIf="!loading && error">{{ error }}</div>

      <div class="body" *ngIf="!loading && profile">

        <!-- Photo hero -->
        <div class="photoWrap">
          <img *ngIf="primaryPhoto" [src]="primaryPhoto.url" [alt]="profile.name" class="heroImg" />
          <div *ngIf="!primaryPhoto" class="heroFallback">{{ (profile.name || '?')[0] }}</div>

          <!-- name overlay -->
          <div class="overlay">
            <div class="nameRow">
              <span class="name">{{ profile.name }}</span>
              <span class="verifiedBadge" *ngIf="profile.isVerified">✓</span>
            </div>
            <div class="sub" *ngIf="profile.location">{{ profile.location }}</div>
            <div class="sub" *ngIf="profile.displayPronouns">{{ profile.displayPronouns }}</div>
          </div>
        </div>

        <!-- Photo strip (if multiple photos) -->
        <div class="photoStrip" *ngIf="allPhotos.length > 1">
          <div class="stripScroll">
            <div
              *ngFor="let p of allPhotos; let i = index"
              class="thumb"
              [class.primary]="i === 0"
            >
              <img [src]="p.url" [alt]="'Photo ' + (i + 1)" />
              <span class="primaryLabel" *ngIf="i === 0">Main</span>
            </div>
          </div>
        </div>

        <!-- Highlights strip -->
        <div class="tilesBlock" *ngIf="highlights.length > 0 || recentTiles.length > 0">

          <div class="tilesRow" *ngIf="highlights.length > 0">
            <div class="tilesHead">
              <span class="sectionLabel">Highlights</span>
              <button class="tilesLink" (click)="goTiles()">My Tiles →</button>
            </div>
            <div class="tilesScroll">
              <div class="tileCard" *ngFor="let t of highlights" (click)="goTiles()">
                <div class="tileSlot">{{ t.highlightSlot }}</div>
                <img *ngIf="t.mediaUrl" [src]="t.mediaUrl" class="tileCardImg" />
                <span *ngIf="!t.mediaUrl && t.contentText" class="tileCardText">{{ t.contentText }}</span>
              </div>
              <button class="tileCard addCard" *ngIf="highlights.length < 9" (click)="goTiles()">
                <span class="addPlus">+</span>
              </button>
            </div>
          </div>

          <div class="tilesRow" *ngIf="recentTiles.length > 0">
            <div class="tilesHead">
              <span class="sectionLabel">Recent</span>
              <button class="tilesLink" (click)="goTiles()">See all →</button>
            </div>
            <div class="tilesScroll">
              <div class="tileCard recent" *ngFor="let t of recentTiles" (click)="goTiles()">
                <div class="activePip"></div>
                <img *ngIf="t.mediaUrl" [src]="t.mediaUrl" class="tileCardImg" />
                <span *ngIf="!t.mediaUrl && t.contentText" class="tileCardText">{{ t.contentText }}</span>
              </div>
            </div>
          </div>

        </div>

        <!-- Intent -->
        <div class="section" *ngIf="profile.intent?.primaryIntent">
          <div class="sectionLabel">Looking for</div>
          <div class="intentChip">{{ profile.intent.primaryIntent }}</div>
          <div class="opennessRow" *ngIf="profile.intent.openness?.length">
            <span class="tag" *ngFor="let o of profile.intent.openness">{{ o }}</span>
          </div>
        </div>

        <!-- Bio -->
        <div class="section" *ngIf="profile.bio">
          <div class="sectionLabel">About</div>
          <div class="bio">{{ profile.bio }}</div>
        </div>

        <!-- Optional chips -->
        <div class="section" *ngIf="chips.length">
          <div class="sectionLabel">Details</div>
          <div class="chipsWrap">
            <span class="chip" *ngFor="let c of chips">
              <span class="chipKey">{{ c.label }}</span>
              <span class="chipVal">{{ c.value }}</span>
            </span>
          </div>
        </div>

        <!-- Actions -->
        <div class="actions">
          <button class="btn" (click)="goEdit()">Edit Profile</button>
          <button class="btn ghost" (click)="goTiles()">My Tiles</button>
          <button class="btn ghost" (click)="goSettings()">Settings</button>
        </div>

      </div>
    </div>
  `,
  styles: [`
    .page {
      min-height: 100dvh;
      padding-bottom: 24px;
    }

    /* ── Loading ─────────────────────────────────────── */
    .loading {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      padding: 60px 16px;
    }

    .loadDot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--gold-400);
      animation: dotPulse 1.2s ease-in-out infinite;
    }
    .loadDot:nth-child(2) { animation-delay: 0.2s; }
    .loadDot:nth-child(3) { animation-delay: 0.4s; }

    @keyframes dotPulse {
      0%, 60%, 100% { transform: scale(1); opacity: 0.4; }
      30% { transform: scale(1.3); opacity: 1; }
    }

    .errState {
      padding: 32px 16px;
      color: var(--rose-300);
      font-family: var(--font-ui);
      font-size: 13px;
      text-align: center;
    }

    /* ── Hero photo ──────────────────────────────────── */
    .photoWrap {
      position: relative;
      width: 100%;
      aspect-ratio: 3 / 4;
      background: var(--bg-elevated);
      overflow: hidden;
    }

    .heroImg {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }

    .heroFallback {
      width: 100%;
      height: 100%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-family: var(--font-display);
      font-size: 72px;
      font-weight: 300;
      color: var(--text-muted);
    }

    .overlay {
      position: absolute;
      bottom: 0;
      left: 0;
      right: 0;
      padding: 40px 18px 18px;
      background: linear-gradient(to top, rgba(14,9,18,0.88) 0%, rgba(14,9,18,0.3) 60%, transparent 100%);
    }

    .nameRow {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .name {
      font-family: var(--font-display);
      font-size: 32px;
      font-weight: 300;
      color: var(--text-primary);
      letter-spacing: -0.025em;
      line-height: 1.1;
    }

    .verifiedBadge {
      width: 22px;
      height: 22px;
      border-radius: 50%;
      background: var(--gold-400);
      color: var(--bg-base);
      font-size: 11px;
      font-weight: 700;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .sub {
      font-family: var(--font-ui);
      font-size: 13px;
      color: var(--text-secondary);
      margin-top: 4px;
    }

    /* ── Photo strip ─────────────────────────────────── */
    .photoStrip {
      padding: 12px 16px 0;
    }

    .stripScroll {
      display: flex;
      gap: 8px;
      overflow-x: auto;
      scrollbar-width: none;
    }
    .stripScroll::-webkit-scrollbar { display: none; }

    .thumb {
      position: relative;
      width: 72px;
      height: 72px;
      border-radius: 12px;
      overflow: hidden;
      flex-shrink: 0;
      border: 1px solid var(--border-subtle);
    }

    .thumb.primary {
      border-color: var(--gold-400);
    }

    .thumb img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .primaryLabel {
      position: absolute;
      bottom: 3px;
      left: 0;
      right: 0;
      text-align: center;
      font-family: var(--font-ui);
      font-size: 9px;
      font-weight: 700;
      color: var(--gold-300);
      letter-spacing: 0.05em;
      text-transform: uppercase;
    }

    /* ── Sections ─────────────────────────────────────── */
    .body {
      display: flex;
      flex-direction: column;
    }

    .section {
      padding: 18px 16px 0;
    }

    .sectionLabel {
      font-family: var(--font-ui);
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 0.2em;
      text-transform: uppercase;
      color: var(--text-muted);
      margin-bottom: 10px;
    }

    .intentChip {
      display: inline-block;
      padding: 8px 16px;
      border-radius: 999px;
      background: rgba(212, 160, 23, 0.1);
      border: 1px solid rgba(212, 160, 23, 0.3);
      color: var(--gold-300);
      font-family: var(--font-ui);
      font-size: 13px;
      font-weight: 600;
    }

    .opennessRow {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-top: 8px;
    }

    .tag {
      padding: 5px 10px;
      border-radius: 999px;
      border: 1px solid var(--border-subtle);
      color: var(--text-muted);
      font-family: var(--font-ui);
      font-size: 11px;
    }

    .bio {
      font-family: var(--font-display);
      font-size: 15px;
      font-weight: 400;
      line-height: 1.6;
      color: var(--text-primary);
      white-space: pre-wrap;
    }

    /* ── Chips ────────────────────────────────────────── */
    .chipsWrap {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }

    .chip {
      display: inline-flex;
      align-items: center;
      gap: 5px;
      padding: 7px 12px;
      border-radius: 999px;
      border: 1px solid var(--border-subtle);
      background: var(--bg-elevated);
      font-family: var(--font-ui);
      font-size: 12px;
    }

    .chipKey {
      color: var(--text-dim);
    }

    .chipVal {
      color: var(--text-secondary);
      font-weight: 600;
    }

    /* ── Tile strips ─────────────────────────────────── */
    .tilesBlock {
      display: flex;
      flex-direction: column;
      gap: 16px;
      padding-top: 18px;
    }

    .tilesRow {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .tilesHead {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 16px;
    }

    .tilesLink {
      font-family: var(--font-ui);
      font-size: 11px;
      font-weight: 600;
      color: var(--text-muted);
      background: transparent;
      border: none;
      cursor: pointer;
      padding: 0;
    }

    .tilesScroll {
      display: flex;
      gap: 8px;
      overflow-x: auto;
      padding: 0 16px;
      scrollbar-width: none;
    }
    .tilesScroll::-webkit-scrollbar { display: none; }

    .tileCard {
      position: relative;
      width: 80px;
      min-width: 80px;
      height: 108px;
      border-radius: 12px;
      background: var(--bg-elevated);
      border: 1px solid var(--border-subtle);
      overflow: hidden;
      flex-shrink: 0;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 6px;
    }

    .tileSlot {
      position: absolute;
      top: 5px;
      right: 6px;
      width: 16px;
      height: 16px;
      border-radius: 50%;
      background: var(--gold-400);
      color: var(--bg-base);
      font-family: var(--font-ui);
      font-size: 9px;
      font-weight: 700;
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 2;
    }

    .tileCardImg {
      width: 100%;
      height: 100%;
      object-fit: cover;
      position: absolute;
      inset: 0;
    }

    .tileCardText {
      font-family: var(--font-display);
      font-size: 10px;
      line-height: 1.4;
      color: var(--text-secondary);
      text-align: center;
      display: -webkit-box;
      -webkit-line-clamp: 6;
      -webkit-box-orient: vertical;
      overflow: hidden;
      z-index: 1;
    }

    .addCard {
      border: 1px dashed var(--border-soft);
      background: transparent;
      flex-direction: column;
      gap: 2px;
    }

    .addPlus {
      font-size: 20px;
      color: var(--text-dim);
      line-height: 1;
    }

    .activePip {
      position: absolute;
      top: 5px;
      left: 6px;
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--plum-400);
      z-index: 2;
    }

    /* ── Actions ──────────────────────────────────────── */
    .actions {
      padding: 24px 16px 8px;
      display: grid;
      gap: 10px;
    }

    .btn {
      width: 100%;
      border: none;
      border-radius: 14px;
      padding: 14px 16px;
      min-height: 48px;
      font-weight: 600;
      font-size: 15px;
      background: linear-gradient(135deg, var(--gold-500), var(--gold-400));
      color: var(--bg-base);
      cursor: pointer;
      transition: all 0.2s ease;
      font-family: var(--font-ui);
    }

    .btn:hover {
      transform: translateY(-1px);
      box-shadow: 0 4px 16px var(--gold-glow);
    }

    .btn.ghost {
      background: var(--bg-surface);
      border: 1px solid var(--border-soft);
      color: var(--text-secondary);
    }

    .btn.ghost:hover {
      border-color: var(--border-medium);
      color: var(--text-primary);
      transform: translateY(-1px);
    }
  `]
})
export class ProfilePageComponent implements OnInit {
  loading = true;
  error = '';
  profile: any = null;
  allPhotos: any[] = [];

  highlights: MyTile[] = [];
  recentTiles: MyTile[] = [];

  constructor(
    private router: Router,
    private onboarding: OnboardingService,
    private tilesService: TilesService,
    private cdr: ChangeDetectorRef,
  ) {}

  async ngOnInit() {
    try {
      const [profileRes] = await Promise.all([
        firstValueFrom(this.onboarding.getReview()),
        this.loadTiles(),
      ]);
      this.profile = profileRes.publicPreview ?? profileRes.self ?? null;
      const photos = profileRes.publicPreview?.photos ?? profileRes.self?.photos ?? profileRes.photos ?? [];
      this.allPhotos = [...photos].sort((a: any, b: any) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0));
    } catch {
      this.error = "Couldn't load your profile.";
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
    }
  }

  private async loadTiles() {
    try {
      const res = await firstValueFrom(this.tilesService.getMine());
      this.highlights = res.tiles
        .filter(t => t.isHighlighted)
        .sort((a, b) => (a.highlightSlot ?? 0) - (b.highlightSlot ?? 0));
      this.recentTiles = res.tiles
        .filter(t => !t.isHighlighted && !t.isExpired)
        .slice(0, 5);
    } catch { /* non-fatal */ }
  }

  get primaryPhoto() {
    return this.allPhotos[0] ?? null;
  }

  get chips(): { label: string; value: string }[] {
    const fields: any[] = this.profile?.optionalPublic ?? [];
    const labelMap: Record<string, string> = {
      job: 'Job', hometown: 'From', education: 'Education', school: 'School',
      pref_height: 'Height', children: 'Children', pets: 'Pets', diet: 'Diet',
      pref_drinking: 'Drinking', pref_smoking: 'Smoking', habits: 'Exercise',
      love_language: 'Love language', languages: 'Languages', mbti: 'MBTI',
    };
    return fields
      .filter((f: any) => f.key && f.value && f.value !== 'hobbies')
      .map((f: any) => ({ label: labelMap[f.key] ?? f.key, value: f.value }));
  }

  goEdit() { this.router.navigateByUrl('/onboarding/review'); }
  goTiles() { this.router.navigateByUrl('/you/tiles'); }
  goSettings() { this.router.navigateByUrl('/you/settings'); }
}
