import {
  Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TilesService, MyTile, ReceivedOrbit } from '../../services/tiles.service';

@Component({
  selector: 'app-my-tiles',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">

      <!-- Header -->
      <div class="topBar">
        <button class="backBtn" (click)="back()">←</button>
        <span class="pageTitle">My Tiles</span>
        <span class="spacer"></span>
      </div>

      <!-- Compose -->
      <div class="compose">
        <textarea
          class="composeInput"
          [(ngModel)]="composeText"
          placeholder="Share a thought anonymously…"
          maxlength="150"
          rows="2"
        ></textarea>
        <div class="composeFoot">
          <span class="composeCount" [class.warn]="composeText.length > 130">
            {{ composeText.length }}/150
          </span>
          <button
            class="composeBtn"
            (click)="post()"
            [disabled]="composeText.trim().length < 10 || posting"
          >{{ posting ? '…' : 'Post' }}</button>
        </div>
      </div>

      <!-- Loading -->
      <div class="loadState" *ngIf="loading">
        <div class="dot"></div><div class="dot"></div><div class="dot"></div>
      </div>

      <!-- Empty -->
      <div class="emptyState" *ngIf="!loading && allTiles.length === 0">
        <div class="emptyIcon">◇</div>
        <div class="emptyTitle">No tiles yet</div>
        <div class="emptySub">Post your first thought above — it'll appear anonymously in Commons for 48 hours.</div>
      </div>

      <!-- HIGHLIGHTED -->
      <div class="group" *ngIf="!loading && highlighted.length">
        <div class="groupLabel">
          <span>HIGHLIGHTED</span>
          <span class="groupCount">{{ highlighted.length }}/9 slots</span>
        </div>
        <div class="tileCard" *ngFor="let t of highlighted">
          <div class="cardHead">
            <span class="statusChip pinned">Slot {{ t.highlightSlot }}</span>
            <span class="orbitCount" *ngIf="orbitMap.get(t.id)">
              ◎ {{ orbitMap.get(t.id) }}
            </span>
          </div>
          <div class="cardBody">
            <img *ngIf="t.mediaUrl" [src]="t.mediaUrl" class="cardImg" />
            <p *ngIf="t.contentText" class="cardText">{{ t.contentText }}</p>
          </div>
          <div class="cardFoot">
            <button class="unpinBtn" (click)="unpinTile(t)" [disabled]="working === t.id">
              {{ working === t.id ? '…' : 'Unpin' }}
            </button>
          </div>
        </div>
      </div>

      <!-- ACTIVE -->
      <div class="group" *ngIf="!loading && active.length">
        <div class="groupLabel">
          <span>ACTIVE</span>
          <span class="groupCount">{{ active.length }} live</span>
        </div>
        <div class="tileCard" *ngFor="let t of active">
          <div class="cardHead">
            <span class="statusChip active">{{ hoursLeft(t.expiresAt) }}</span>
            <span class="orbitCount" *ngIf="orbitMap.get(t.id)">
              ◎ {{ orbitMap.get(t.id) }}
            </span>
          </div>
          <div class="cardBody">
            <img *ngIf="t.mediaUrl" [src]="t.mediaUrl" class="cardImg" />
            <p *ngIf="t.contentText" class="cardText">{{ t.contentText }}</p>
          </div>
        </div>
      </div>

      <!-- EXPIRED -->
      <div class="group" *ngIf="!loading && expired.length">
        <div class="groupLabel">
          <span>EXPIRED</span>
          <span class="groupHint">Pin your best ones</span>
        </div>
        <div class="tileCard" *ngFor="let t of expired">
          <div class="cardHead">
            <span class="statusChip expired">Expired</span>
            <span class="orbitCount" *ngIf="orbitMap.get(t.id)">
              ◎ {{ orbitMap.get(t.id) }}
            </span>
          </div>
          <div class="cardBody">
            <img *ngIf="t.mediaUrl" [src]="t.mediaUrl" class="cardImg" />
            <p *ngIf="t.contentText" class="cardText">{{ t.contentText }}</p>
          </div>
          <div class="cardFoot">
            <button
              class="pinBtn"
              (click)="openSlotPicker(t)"
              [disabled]="working === t.id || highlighted.length >= 9"
            >
              {{ working === t.id ? '…' : (highlighted.length >= 9 ? 'Slots full' : 'Pin to profile') }}
            </button>
          </div>
        </div>
      </div>

    </div>

    <!-- Toast -->
    <div class="toast" *ngIf="toast">{{ toast }}</div>

    <!-- Slot picker sheet -->
    <div class="overlay" *ngIf="pickingFor" (click)="closeSlotPicker()">
      <div class="sheet" (click)="$event.stopPropagation()">
        <div class="sheetTitle">Choose a slot</div>
        <div class="sheetSub">Slot 1 appears first on your profile</div>
        <div class="slotGrid">
          <button
            class="slotBtn"
            *ngFor="let s of slots"
            [class.occupied]="occupiedSlots.has(s)"
            [disabled]="slotWorking === s"
            (click)="confirmPin(s)"
          >
            <span class="slotNum">{{ s }}</span>
            <span class="slotOccLabel" *ngIf="occupiedSlots.has(s)">•</span>
          </button>
        </div>
        <button class="sheetCancel" (click)="closeSlotPicker()">Cancel</button>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .page {
      min-height: 100vh;
      background: var(--bg-base);
      color: var(--text-primary);
      font-family: var(--font-ui);
      padding-bottom: 80px;
    }

    /* ── Header ── */
    .topBar {
      position: sticky;
      top: 0;
      z-index: 10;
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 52px 20px 14px;
      background: var(--bg-base);
      border-bottom: 1px solid var(--border-subtle);
    }
    .backBtn {
      width: 36px; height: 36px;
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-md);
      background: var(--bg-surface);
      color: var(--text-primary);
      font-size: 16px;
      cursor: pointer;
      display: flex; align-items: center; justify-content: center;
    }
    .pageTitle {
      font-size: var(--text-lg);
      font-weight: 700;
      letter-spacing: -0.01em;
    }
    .spacer { flex: 1; }

    /* ── Compose ── */
    .compose {
      margin: 16px 16px 0;
      background: var(--bg-elevated);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      padding: 14px;
    }
    .composeInput {
      width: 100%;
      background: transparent;
      border: none;
      outline: none;
      color: var(--text-primary);
      font-family: var(--font-display);
      font-size: 15px;
      line-height: 1.55;
      resize: none;
      box-sizing: border-box;
    }
    .composeInput::placeholder { color: var(--text-dim); }
    .composeFoot {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-top: 8px;
    }
    .composeCount {
      font-size: 11px;
      color: var(--text-dim);
    }
    .composeCount.warn { color: var(--rose-300); }
    .composeBtn {
      padding: 7px 18px;
      background: linear-gradient(135deg, var(--plum-500), var(--plum-400));
      color: var(--text-primary);
      border: none;
      border-radius: var(--radius-full);
      font-family: var(--font-ui);
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
    }
    .composeBtn:disabled { opacity: 0.4; cursor: default; }

    /* ── Load state ── */
    .loadState {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      padding: 48px 0;
    }
    .dot {
      width: 7px; height: 7px;
      border-radius: 50%;
      background: var(--plum-400);
      animation: dotBounce 1.2s ease-in-out infinite;
    }
    .dot:nth-child(2) { animation-delay: 0.2s; }
    .dot:nth-child(3) { animation-delay: 0.4s; }
    @keyframes dotBounce {
      0%, 60%, 100% { transform: translateY(0); opacity: 0.4; }
      30% { transform: translateY(-6px); opacity: 1; }
    }

    /* ── Empty ── */
    .emptyState {
      text-align: center;
      padding: 64px 24px;
    }
    .emptyIcon { font-size: 32px; color: var(--text-dim); margin-bottom: 12px; }
    .emptyTitle {
      font-family: var(--font-display);
      font-size: 20px;
      font-weight: 400;
      margin-bottom: 8px;
    }
    .emptySub {
      font-size: 13px;
      color: var(--text-muted);
      line-height: 1.55;
      max-width: 34ch;
      margin: 0 auto;
    }

    /* ── Group ── */
    .group {
      padding: 20px 16px 0;
      display: flex;
      flex-direction: column;
      gap: 10px;
    }
    .groupLabel {
      display: flex;
      align-items: center;
      justify-content: space-between;
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 0.12em;
      color: var(--text-muted);
    }
    .groupCount, .groupHint {
      font-weight: 400;
      letter-spacing: 0.02em;
      color: var(--text-dim);
    }

    /* ── Tile card ── */
    .tileCard {
      background: var(--bg-surface);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      overflow: hidden;
    }
    .cardHead {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 12px 14px 0;
    }
    .statusChip {
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 0.08em;
      padding: 3px 10px;
      border-radius: var(--radius-full);
    }
    .statusChip.active {
      background: rgba(127, 119, 221, 0.12);
      border: 1px solid rgba(127, 119, 221, 0.25);
      color: var(--plum-300);
    }
    .statusChip.expired {
      background: rgba(200, 180, 160, 0.08);
      border: 1px solid var(--border-subtle);
      color: var(--text-dim);
    }
    .statusChip.pinned {
      background: rgba(212, 160, 23, 0.12);
      border: 1px solid rgba(212, 160, 23, 0.25);
      color: var(--gold-300);
    }
    .orbitCount {
      font-size: 11px;
      color: var(--text-muted);
      font-weight: 600;
    }
    .cardBody {
      padding: 10px 14px;
    }
    .cardText {
      font-family: var(--font-display);
      font-size: 15px;
      line-height: 1.6;
      color: var(--text-primary);
      margin: 0;
      white-space: pre-wrap;
    }
    .cardImg {
      width: 100%;
      border-radius: var(--radius-md);
      object-fit: cover;
      max-height: 200px;
      display: block;
    }
    .cardFoot {
      padding: 0 14px 12px;
    }
    .pinBtn {
      width: 100%;
      height: 38px;
      border-radius: var(--radius-md);
      border: 1px solid rgba(212, 160, 23, 0.30);
      background: rgba(212, 160, 23, 0.08);
      color: var(--gold-300);
      font-family: var(--font-ui);
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
    }
    .pinBtn:disabled { opacity: 0.4; cursor: default; }
    .unpinBtn {
      border: 1px solid var(--border-subtle);
      background: transparent;
      color: var(--text-muted);
      font-family: var(--font-ui);
      font-size: 12px;
      font-weight: 600;
      padding: 5px 14px;
      border-radius: var(--radius-md);
      cursor: pointer;
    }
    .unpinBtn:disabled { opacity: 0.4; cursor: default; }

    /* ── Toast ── */
    .toast {
      position: fixed;
      bottom: 80px;
      left: 50%;
      transform: translateX(-50%);
      background: var(--bg-elevated);
      color: var(--text-primary);
      font-family: var(--font-ui);
      font-size: 13px;
      padding: 10px 20px;
      border-radius: var(--radius-full);
      border: 1px solid var(--border-soft);
      pointer-events: none;
      white-space: nowrap;
      z-index: 50;
    }

    /* ── Slot picker ── */
    .overlay {
      position: fixed;
      inset: 0;
      background: rgba(0,0,0,0.72);
      z-index: 100;
      display: flex;
      align-items: flex-end;
    }
    .sheet {
      width: 100%;
      background: var(--bg-surface);
      border-top: 1px solid var(--border-soft);
      border-radius: var(--radius-xl) var(--radius-xl) 0 0;
      padding: 24px 20px 48px;
    }
    .sheetTitle {
      font-family: var(--font-display);
      font-size: var(--text-xl);
      font-weight: 700;
      margin-bottom: 4px;
    }
    .sheetSub {
      font-size: var(--text-sm);
      color: var(--text-muted);
      margin-bottom: 20px;
    }
    .slotGrid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 10px;
      margin-bottom: 16px;
    }
    .slotBtn {
      aspect-ratio: 1;
      border-radius: var(--radius-md);
      background: var(--bg-elevated);
      border: 1px solid var(--border-subtle);
      color: var(--text-primary);
      font-family: var(--font-ui);
      font-size: 18px;
      font-weight: 700;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-direction: column;
      gap: 2px;
      transition: background 150ms, border-color 150ms;
      position: relative;
    }
    .slotBtn:hover:not(:disabled) {
      background: rgba(212, 160, 23, 0.10);
      border-color: rgba(212, 160, 23, 0.30);
    }
    .slotBtn.occupied {
      border-color: rgba(212, 160, 23, 0.40);
    }
    .slotNum { font-size: 20px; font-weight: 700; }
    .slotOccLabel {
      position: absolute;
      top: 6px;
      right: 8px;
      font-size: 10px;
      color: var(--gold-300);
    }
    .sheetCancel {
      width: 100%;
      height: 48px;
      border-radius: var(--radius-md);
      background: var(--bg-elevated);
      border: none;
      color: var(--text-secondary);
      font-family: var(--font-ui);
      font-size: var(--text-sm);
      font-weight: 600;
      cursor: pointer;
    }
  `]
})
export class MyTilesPageComponent implements OnInit {
  allTiles: MyTile[] = [];
  orbitMap = new Map<string, number>();
  loading = true;
  working: string | null = null;
  slotWorking: number | null = null;
  toast = '';
  private toastTimer: ReturnType<typeof setTimeout> | null = null;

  composeText = '';
  posting = false;

  pickingFor: MyTile | null = null;
  readonly slots = [1, 2, 3, 4, 5, 6, 7, 8, 9];

  constructor(
    private tiles: TilesService,
    private router: Router,
    private cdr: ChangeDetectorRef,
  ) {}

  async ngOnInit() {
    await this.load();
  }

  private async load() {
    this.loading = true;
    this.cdr.markForCheck();
    try {
      const [tileRes, orbits] = await Promise.all([
        firstValueFrom(this.tiles.getMine()),
        firstValueFrom(this.tiles.getReceivedOrbits()).catch(() => [] as any[])
      ]);
      this.allTiles = tileRes.tiles;
      this.orbitMap = new Map<string, number>();
      for (const o of orbits as ReceivedOrbit[]) {
        this.orbitMap.set(o.tileId, (this.orbitMap.get(o.tileId) ?? 0) + 1);
      }
    } catch {
      this.showToast('Could not load tiles');
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
    }
  }

  get highlighted() {
    return this.allTiles
      .filter(t => t.isHighlighted)
      .sort((a, b) => (a.highlightSlot ?? 0) - (b.highlightSlot ?? 0));
  }

  get active() {
    return this.allTiles.filter(t => !t.isHighlighted && !t.isExpired);
  }

  get expired() {
    return this.allTiles.filter(t => !t.isHighlighted && t.isExpired);
  }

  get occupiedSlots(): Set<number> {
    return new Set(this.highlighted.map(t => t.highlightSlot!).filter(Boolean));
  }

  hoursLeft(expiresAt: string): string {
    const ms = new Date(expiresAt).getTime() - Date.now();
    if (ms <= 0) return 'Expiring';
    const h = Math.floor(ms / 3600000);
    const m = Math.floor((ms % 3600000) / 60000);
    return h > 0 ? `${h}h left` : `${m}m left`;
  }

  async post() {
    const text = this.composeText.trim();
    if (text.length < 10 || text.length > 150 || this.posting) return;
    this.posting = true;
    this.cdr.markForCheck();
    try {
      await firstValueFrom(this.tiles.create('text', text));
      this.composeText = '';
      this.showToast('Posted to Commons');
      await this.load();
    } catch {
      this.showToast('Could not post — try again');
    } finally {
      this.posting = false;
      this.cdr.markForCheck();
    }
  }

  openSlotPicker(tile: MyTile) {
    this.pickingFor = tile;
    this.cdr.markForCheck();
  }

  closeSlotPicker() {
    this.pickingFor = null;
    this.slotWorking = null;
    this.cdr.markForCheck();
  }

  async confirmPin(slot: number) {
    if (!this.pickingFor || this.slotWorking !== null) return;
    const tile = this.pickingFor;
    this.slotWorking = slot;
    this.working = tile.id;
    this.cdr.markForCheck();
    try {
      await firstValueFrom(this.tiles.highlight(tile.id, slot));
      this.closeSlotPicker();
      this.showToast(`Pinned to slot ${slot}`);
      await this.load();
    } catch (err: any) {
      this.closeSlotPicker();
      this.showToast(err?.error?.error ?? 'Could not pin tile');
    } finally {
      this.working = null;
      this.cdr.markForCheck();
    }
  }

  async unpinTile(tile: MyTile) {
    if (this.working) return;
    this.working = tile.id;
    this.cdr.markForCheck();
    try {
      await firstValueFrom(this.tiles.unhighlight(tile.id));
      this.showToast('Removed from highlights');
      await this.load();
    } catch {
      this.showToast('Could not unpin');
    } finally {
      this.working = null;
      this.cdr.markForCheck();
    }
  }

  private showToast(msg: string) {
    this.toast = msg;
    this.cdr.markForCheck();
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => {
      this.toast = '';
      this.cdr.markForCheck();
    }, 2500);
  }

  back() { this.router.navigateByUrl('/you'); }
}
