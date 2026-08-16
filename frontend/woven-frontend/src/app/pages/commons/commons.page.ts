import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { CommonsService, CommonsTile } from '../../services/commons.service';

const SESSION_KEY = 'woven_commons_session';

@Component({
  selector: 'app-commons',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="commons">

      <!-- Header -->
      <div class="head">
        <div>
          <div class="kicker">◇ Anonymous</div>
          <h2 class="title">Commons</h2>
          <p class="sub">Discover what resonates.</p>
        </div>
        <button class="refreshBtn" (click)="refresh()" [class.spinning]="refreshing" title="Refresh">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
            <polyline points="23 4 23 10 17 10"/>
            <path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/>
          </svg>
        </button>
      </div>

      <!-- Energy bar -->
      <div class="energyWrap" *ngIf="!loading && tiles.length">
        <div class="energyTrack">
          <div class="energyFill" [style.width.%]="energyPct"></div>
        </div>
        <span class="energyLabel">{{ DAILY_CAP - tilesViewedToday }} views left today</span>
      </div>

      <!-- Compose -->
      <div class="compose" *ngIf="!loading && !energyDepleted">
        <textarea
          class="composeInput"
          [(ngModel)]="composeText"
          placeholder="Share a thought, anonymously…"
          maxlength="150"
          rows="2"
        ></textarea>
        <div class="composeFooter">
          <span class="composeCount" [class.warn]="composeText.length > 130">{{ composeText.length }}/150</span>
          <button class="composeBtn" (click)="postTile()"
            [disabled]="composeText.trim().length < 10 || posting">
            {{ posting ? '…' : 'Post' }}
          </button>
        </div>
      </div>

      <!-- Loading -->
      <div class="loadState" *ngIf="loading">
        <div class="dot"></div><div class="dot"></div><div class="dot"></div>
      </div>

      <!-- Error -->
      <div class="stateMsg" *ngIf="!loading && error">{{ error }}</div>

      <!-- Energy depleted -->
      <div class="stateBlock" *ngIf="!loading && energyDepleted">
        <div class="stateIcon">◇</div>
        <div class="stateTitle">You've seen it all today</div>
        <div class="stateSub">Commons resets at midnight UTC.</div>
      </div>

      <!-- Empty -->
      <div class="stateBlock" *ngIf="!loading && !error && !energyDepleted && tiles.length === 0">
        <div class="stateIcon">◇</div>
        <div class="stateTitle">Nothing here yet</div>
        <div class="stateSub">As more people join, their anonymous thoughts will appear here.</div>
      </div>

      <!-- 3×3 Grid -->
      <div class="grid" *ngIf="!loading && tiles.length">
        <div
          class="cell"
          *ngFor="let t of tiles"
          (click)="openTile(t)"
        >
          <!-- Photo cell -->
          <img
            *ngIf="t.mediaUrl && t.contentType !== 'video'"
            class="cellImg"
            [src]="t.mediaUrl"
            alt=""
            loading="lazy"
          />

          <!-- Video cell -->
          <div class="cellVideo" *ngIf="t.mediaUrl && t.contentType === 'video'">
            <video [src]="t.mediaUrl" muted playsinline preload="none"></video>
            <div class="playIcon">▶</div>
          </div>

          <!-- Text cell -->
          <div class="cellText" *ngIf="!t.mediaUrl">
            <span class="cellPreview">{{ t.contentText | slice:0:80 }}</span>
          </div>

          <!-- Resonance dot -->
          <div class="dot" [class.resonant]="isResonant(t)" [class.discovery]="!isResonant(t)"></div>
        </div>

        <!-- Load more -->
        <button class="loadMoreBtn" *ngIf="hasMore && !loadingMore" (click)="loadMore()">
          Load more
        </button>
        <div class="loadState small" *ngIf="loadingMore">
          <div class="dot"></div><div class="dot"></div><div class="dot"></div>
        </div>
        <div class="endLine" *ngIf="!hasMore && !energyDepleted && tiles.length">
          You're all caught up ◇
        </div>
      </div>

    </div>

    <!-- Tile drawer -->
    <div class="backdrop" *ngIf="activeTile" (click)="closeDrawer()">
      <div class="drawer" (click)="$event.stopPropagation()">
        <div class="drawerHandle"></div>

        <div class="drawerHead">
          <span class="badge" [class.resonant]="isResonant(activeTile)" [class.discovery]="!isResonant(activeTile)">
            {{ isResonant(activeTile) ? '◈ resonant' : '◇ discovery' }}
          </span>
          <span class="tileTime">{{ timeAgo(activeTile.createdAt) }}</span>
          <button class="closeBtn" (click)="closeDrawer()">✕</button>
        </div>

        <!-- Photo -->
        <img
          *ngIf="activeTile.mediaUrl && activeTile.contentType !== 'video'"
          class="drawerImg"
          [src]="activeTile.mediaUrl"
          alt=""
        />

        <!-- Video -->
        <video
          *ngIf="activeTile.mediaUrl && activeTile.contentType === 'video'"
          class="drawerVideo"
          [src]="activeTile.mediaUrl"
          controls
          playsinline
        ></video>

        <!-- Text -->
        <div class="drawerBody" *ngIf="activeTile.contentText">{{ activeTile.contentText }}</div>

        <!-- Orbit -->
        <div class="drawerFoot">
          <button
            class="orbitBtn"
            [class.active]="orbitedTiles.has(activeTile.tileId)"
            [disabled]="orbitedTiles.has(activeTile.tileId) || orbitingTiles.has(activeTile.tileId)"
            (click)="orbitTile(activeTile)"
          >
            <span class="orbitIcon">{{ orbitedTiles.has(activeTile.tileId) ? '◈' : '◎' }}</span>
            <span>{{ orbitedTiles.has(activeTile.tileId) ? 'Orbiting' : (orbitingTiles.has(activeTile.tileId) ? '…' : 'Orbit') }}</span>
          </button>
        </div>
      </div>
    </div>

    <div class="toast" *ngIf="toast">{{ toast }}</div>
  `,
  styles: [`
    :host { display: block; }

    .commons { display: grid; gap: 14px; padding-bottom: 100px; }

    /* ── Header ── */
    .head {
      display: flex; justify-content: space-between; align-items: flex-start; gap: 12px;
    }
    .kicker {
      font-size: 10px; font-weight: 700; letter-spacing: 0.28em;
      text-transform: uppercase; color: var(--text-muted); font-family: var(--font-ui);
    }
    .title {
      font-family: var(--font-display); font-size: 20px; font-weight: 400;
      letter-spacing: -0.025em; color: var(--text-primary); margin: 4px 0 0;
    }
    .sub { font-size: 13px; color: var(--text-muted); margin: 4px 0 0; font-family: var(--font-ui); }
    .refreshBtn {
      width: 34px; height: 34px; border-radius: 50%; border: 1px solid var(--border-soft);
      background: var(--bg-elevated); color: var(--text-muted);
      display: flex; align-items: center; justify-content: center;
      cursor: pointer; flex-shrink: 0; transition: color 0.15s;
    }
    .refreshBtn:hover { color: var(--text-secondary); }
    .refreshBtn.spinning { animation: spin 0.7s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* ── Energy bar ── */
    .energyWrap { display: flex; align-items: center; gap: 10px; }
    .energyTrack {
      flex: 1; height: 2px; background: var(--border-subtle);
      border-radius: 9999px; overflow: hidden;
    }
    .energyFill {
      height: 100%;
      background: linear-gradient(90deg, var(--plum-400), var(--gold-400));
      border-radius: 9999px; transition: width 0.4s ease;
    }
    .energyLabel { font-family: var(--font-ui); font-size: 11px; color: var(--text-dim); white-space: nowrap; }

    /* ── Compose ── */
    .compose {
      background: var(--bg-elevated); border: 1px solid var(--border-subtle);
      border-radius: 16px; padding: 12px; display: grid; gap: 8px;
    }
    .composeInput {
      width: 100%; background: transparent; border: none; outline: none;
      color: var(--text-primary); font-family: var(--font-display); font-size: 15px;
      line-height: 1.5; resize: none; padding: 0; box-sizing: border-box;
    }
    .composeInput::placeholder { color: var(--text-dim); }
    .composeFooter { display: flex; align-items: center; justify-content: space-between; }
    .composeCount { font-family: var(--font-ui); font-size: 11px; color: var(--text-dim); }
    .composeCount.warn { color: var(--rose-300); }
    .composeBtn {
      padding: 6px 16px; background: linear-gradient(135deg, var(--plum-500), var(--plum-400));
      color: var(--text-primary); border: none; border-radius: 999px;
      font-family: var(--font-ui); font-size: 13px; font-weight: 600; cursor: pointer;
    }
    .composeBtn:disabled { opacity: 0.4; cursor: default; }

    /* ── States ── */
    .loadState {
      display: flex; align-items: center; justify-content: center;
      gap: 6px; padding: 32px 0;
    }
    .loadState.small { padding: 16px 0; }
    .dot {
      width: 7px; height: 7px; border-radius: 50%; background: var(--plum-400);
      animation: dotBounce 1.2s ease-in-out infinite;
    }
    .dot:nth-child(2) { animation-delay: 0.2s; }
    .dot:nth-child(3) { animation-delay: 0.4s; }
    @keyframes dotBounce {
      0%, 60%, 100% { transform: translateY(0); opacity: 0.4; }
      30% { transform: translateY(-6px); opacity: 1; }
    }
    .stateMsg { padding: 24px 0; font-size: 13px; color: var(--rose-300); text-align: center; }
    .stateBlock { text-align: center; padding: 48px 16px; }
    .stateIcon { font-size: 28px; color: var(--text-dim); margin-bottom: 10px; }
    .stateTitle { font-family: var(--font-display); font-size: 18px; color: var(--text-primary); margin-bottom: 6px; }
    .stateSub { font-family: var(--font-ui); font-size: 13px; color: var(--text-muted); line-height: 1.5; }

    /* ── 3×3 Grid ── */
    .grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 3px;
    }

    .cell {
      position: relative;
      aspect-ratio: 1;
      background: var(--bg-elevated);
      border-radius: 4px;
      overflow: hidden;
      cursor: pointer;
    }
    .cell:active { opacity: 0.85; }

    .cellImg {
      width: 100%; height: 100%; object-fit: cover; display: block;
    }

    .cellVideo {
      width: 100%; height: 100%; position: relative;
      background: var(--bg-surface); display: flex; align-items: center; justify-content: center;
    }
    .cellVideo video { width: 100%; height: 100%; object-fit: cover; }
    .playIcon {
      position: absolute; inset: 0; display: flex; align-items: center; justify-content: center;
      font-size: 18px; color: rgba(255,255,255,0.9);
      text-shadow: 0 1px 4px rgba(0,0,0,0.5);
    }

    .cellText {
      width: 100%; height: 100%; padding: 10px;
      background: var(--bg-elevated);
      display: flex; align-items: flex-start;
      box-sizing: border-box;
    }
    .cellPreview {
      font-family: var(--font-display); font-size: 11px; line-height: 1.45;
      color: var(--text-secondary); overflow: hidden;
      display: -webkit-box; -webkit-line-clamp: 5; -webkit-box-orient: vertical;
    }

    /* Resonance dot — top-right corner */
    .dot {
      position: absolute; top: 5px; right: 5px;
      width: 6px; height: 6px; border-radius: 50%;
      animation: none; /* override loadState dot animation */
      flex-shrink: 0;
    }
    .dot.resonant { background: var(--gold-400); }
    .dot.discovery { background: var(--plum-400); }

    /* ── Load more / end ── */
    .loadMoreBtn {
      grid-column: 1 / -1;
      padding: 14px; background: var(--bg-elevated); border: 1px solid var(--border-soft);
      border-radius: 12px; color: var(--text-secondary); font-family: var(--font-ui);
      font-size: 14px; font-weight: 600; cursor: pointer; margin-top: 4px;
    }
    .endLine {
      grid-column: 1 / -1; text-align: center; font-family: var(--font-ui);
      font-size: 12px; color: var(--text-dim); padding: 12px 0; letter-spacing: 0.04em;
    }

    /* ── Backdrop ── */
    .backdrop {
      position: fixed; inset: 0; background: rgba(0,0,0,0.65);
      z-index: 100; display: flex; align-items: flex-end;
      animation: fadeIn 0.15s ease;
    }
    @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }

    /* ── Drawer ── */
    .drawer {
      width: 100%; background: var(--bg-surface);
      border-radius: 20px 20px 0 0;
      border-top: 1px solid var(--border-soft);
      padding: 12px 20px 48px;
      max-height: 85vh; overflow-y: auto;
      animation: slideUp 0.22s cubic-bezier(0.32, 0.72, 0, 1);
    }
    @keyframes slideUp { from { transform: translateY(100%); } to { transform: translateY(0); } }

    .drawerHandle {
      width: 36px; height: 4px; background: var(--border-soft);
      border-radius: 9999px; margin: 0 auto 16px;
    }

    .drawerHead {
      display: flex; align-items: center; gap: 10px; margin-bottom: 16px;
    }
    .badge {
      font-family: var(--font-ui); font-size: 10px; font-weight: 700;
      letter-spacing: 0.1em; text-transform: uppercase;
      padding: 3px 10px; border-radius: 999px;
    }
    .badge.resonant {
      background: rgba(212,160,23,0.12); border: 1px solid rgba(212,160,23,0.28);
      color: var(--gold-300);
    }
    .badge.discovery {
      background: rgba(127,119,221,0.12); border: 1px solid rgba(127,119,221,0.28);
      color: var(--plum-300);
    }
    .tileTime { font-family: var(--font-ui); font-size: 11px; color: var(--text-dim); flex: 1; }
    .closeBtn {
      width: 28px; height: 28px; border-radius: 50%; border: 1px solid var(--border-subtle);
      background: var(--bg-elevated); color: var(--text-muted); font-size: 13px;
      cursor: pointer; display: flex; align-items: center; justify-content: center;
    }

    .drawerImg {
      width: 100%; border-radius: 12px; max-height: 340px;
      object-fit: cover; display: block; margin-bottom: 14px;
    }
    .drawerVideo {
      width: 100%; border-radius: 12px; max-height: 340px;
      display: block; margin-bottom: 14px; background: #000;
    }
    .drawerBody {
      font-family: var(--font-display); font-size: 16px; font-weight: 400;
      line-height: 1.65; color: var(--text-primary); white-space: pre-wrap;
      margin-bottom: 20px;
    }

    .drawerFoot { display: flex; justify-content: flex-end; padding-top: 4px; }

    .orbitBtn {
      display: flex; align-items: center; gap: 6px;
      padding: 8px 18px; border-radius: 999px;
      background: var(--bg-elevated); border: 1px solid var(--border-soft);
      color: var(--text-secondary); font-family: var(--font-ui);
      font-size: 13px; font-weight: 600; cursor: pointer;
      transition: background 150ms, border-color 150ms, color 150ms;
    }
    .orbitBtn:hover:not(:disabled) {
      background: rgba(127,119,221,0.1); border-color: rgba(127,119,221,0.35); color: var(--plum-300);
    }
    .orbitBtn.active {
      background: rgba(212,160,23,0.1); border-color: rgba(212,160,23,0.35); color: var(--gold-300);
    }
    .orbitBtn:disabled { cursor: default; opacity: 0.6; }
    .orbitIcon { font-size: 14px; }

    /* ── Toast ── */
    .toast {
      position: fixed; bottom: 80px; left: 50%; transform: translateX(-50%);
      background: var(--bg-overlay); color: var(--text-primary);
      font-family: var(--font-ui); font-size: 13px;
      padding: 10px 20px; border-radius: 999px;
      border: 1px solid var(--border-soft); backdrop-filter: blur(12px);
      pointer-events: none; white-space: nowrap; z-index: 200;
      animation: toastIn 0.2s ease;
    }
    @keyframes toastIn {
      from { opacity: 0; transform: translateX(-50%) translateY(6px); }
      to   { opacity: 1; transform: translateX(-50%) translateY(0); }
    }
  `],
})
export class CommonsPageComponent implements OnInit, OnDestroy {
  tiles: CommonsTile[] = [];
  loading = true;
  loadingMore = false;
  refreshing = false;
  energyDepleted = false;
  error = '';
  toast = '';

  page = 1;
  sessionId = '';
  hasMore = true;

  composeText = '';
  posting = false;

  readonly DAILY_CAP = 100;
  tilesViewedToday = 0;

  activeTile: CommonsTile | null = null;
  private drawerOpenAt = 0;

  orbitedTiles = new Set<string>();
  orbitingTiles = new Set<string>();

  private toastTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(private commons: CommonsService, private cdr: ChangeDetectorRef) {}

  async ngOnInit() {
    // Restore session from sessionStorage so page refresh reuses the Redis-cached feed
    this.sessionId = sessionStorage.getItem(SESSION_KEY) ?? '';
    await this.loadFeed();
  }

  ngOnDestroy() {
    // Flush dwell if drawer is still open
    if (this.activeTile && this.drawerOpenAt > 0) {
      this.recordDwell(this.activeTile.tileId);
    }
    if (this.toastTimer) clearTimeout(this.toastTimer);
  }

  async loadFeed() {
    this.loading = true;
    this.error = '';
    this.energyDepleted = false;
    this.cdr.markForCheck();
    try {
      const res = await firstValueFrom(
        this.commons.getFeed(1, this.sessionId || undefined)
      );
      this.sessionId = res.sessionId;
      sessionStorage.setItem(SESSION_KEY, res.sessionId);
      this.page = 1;
      this.tiles = res.tiles;
      this.hasMore = res.tiles.length === 20;
      this.tilesViewedToday += res.tiles.length;
    } catch (err: any) {
      if (err?.status === 429) {
        this.energyDepleted = true;
        this.tilesViewedToday = this.DAILY_CAP;
      } else {
        this.error = "Couldn't load Commons right now.";
      }
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
    }
  }

  async loadMore() {
    if (this.loadingMore || !this.hasMore) return;
    this.loadingMore = true;
    this.cdr.markForCheck();
    try {
      const res = await firstValueFrom(this.commons.getFeed(this.page + 1, this.sessionId));
      this.page++;
      this.tiles = [...this.tiles, ...res.tiles];
      this.hasMore = res.tiles.length === 20;
      this.tilesViewedToday += res.tiles.length;
    } catch (err: any) {
      if (err?.status === 429) { this.energyDepleted = true; this.hasMore = false; }
    } finally {
      this.loadingMore = false;
      this.cdr.markForCheck();
    }
  }

  async refresh() {
    if (this.refreshing) return;
    this.refreshing = true;
    sessionStorage.removeItem(SESSION_KEY);
    this.sessionId = '';
    this.cdr.markForCheck();
    try {
      await firstValueFrom(this.commons.refresh());
    } catch { /* ignore */ }
    this.tiles = [];
    this.tilesViewedToday = 0;
    await this.loadFeed();
    this.refreshing = false;
    this.cdr.markForCheck();
  }

  // ── Drawer ──────────────────────────────────────────────────────────────────

  openTile(tile: CommonsTile) {
    this.activeTile = tile;
    this.drawerOpenAt = performance.now();
    this.cdr.markForCheck();
  }

  closeDrawer() {
    if (!this.activeTile) return;
    this.recordDwell(this.activeTile.tileId);
    this.activeTile = null;
    this.drawerOpenAt = 0;
    this.cdr.markForCheck();
  }

  private recordDwell(tileId: string) {
    const duration = this.drawerOpenAt > 0
      ? Math.round(performance.now() - this.drawerOpenAt)
      : undefined;
    this.commons.recordView(tileId, duration).subscribe({ error: () => {} });
  }

  // ── Orbit ───────────────────────────────────────────────────────────────────

  orbitTile(tile: CommonsTile) {
    if (this.orbitedTiles.has(tile.tileId) || this.orbitingTiles.has(tile.tileId)) return;
    this.orbitingTiles.add(tile.tileId);
    this.cdr.markForCheck();

    this.commons.orbitTile(tile.tileId).subscribe({
      next: res => {
        this.orbitingTiles.delete(tile.tileId);
        this.orbitedTiles.add(tile.tileId);
        this.showToast(res.mutualDetected ? '◈ A mutual pull — you both feel it' : '◎ Orbiting this thought');
        this.cdr.markForCheck();
      },
      error: (err: any) => {
        this.orbitingTiles.delete(tile.tileId);
        if (err?.error?.error === 'ALREADY_ORBITED') this.orbitedTiles.add(tile.tileId);
        else if (err?.status === 429) this.showToast('Orbit limit reached for today');
        else if (err?.error?.error === 'CANNOT_ORBIT_OWN_TILE') this.showToast("Can't orbit your own tile");
        this.cdr.markForCheck();
      }
    });
  }

  // ── Compose ─────────────────────────────────────────────────────────────────

  async postTile() {
    const text = this.composeText.trim();
    if (text.length < 10 || text.length > 150 || this.posting) return;
    this.posting = true;
    this.cdr.markForCheck();
    try {
      await firstValueFrom(this.commons.createTile('text', text));
      this.composeText = '';
      this.showToast('Posted anonymously');
    } catch {
      this.showToast('Could not post — try again');
    } finally {
      this.posting = false;
      this.cdr.markForCheck();
    }
  }

  // ── Helpers ─────────────────────────────────────────────────────────────────

  isResonant(tile: CommonsTile) { return tile.similarity >= 0.65; }

  timeAgo(isoDate: string): string {
    const ms = Date.now() - new Date(isoDate).getTime();
    const m = Math.floor(ms / 60000);
    if (m < 1) return 'just now';
    if (m < 60) return `${m}m ago`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h}h ago`;
    return `${Math.floor(h / 24)}d ago`;
  }

  private showToast(msg: string) {
    this.toast = msg;
    this.cdr.markForCheck();
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => { this.toast = ''; this.cdr.markForCheck(); }, 2500);
  }

  get energyPct() {
    return Math.max(0, Math.min(100, ((this.DAILY_CAP - this.tilesViewedToday) / this.DAILY_CAP) * 100));
  }
}
