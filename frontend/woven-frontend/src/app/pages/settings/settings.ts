import {
  Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { PushService } from '../../services/push.service';
import { CoachingService } from '../../services/coaching.service';
import { firstValueFrom } from 'rxjs';

interface BlockedUser {
  userId:    number;
  name:      string;
  photo:     string | null;
  blockedAt: string;
}

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">

      <!-- Header -->
      <div class="topBar">
        <button class="backBtn" (click)="back()">←</button>
        <span class="pageTitle">Settings</span>
        <span class="spacer"></span>
      </div>

      <div class="content">

        <!-- ACCOUNT -->
        <div class="section">
          <div class="sectionLabel">ACCOUNT</div>

          <div class="card">
            <button class="row" (click)="logout()">
              <span class="rowLabel">Log out</span>
              <span class="rowArrow">→</span>
            </button>

            <div class="divider"></div>

            <button class="row danger" (click)="startDelete()">
              <span class="rowLabel">Delete account</span>
              <span class="rowArrow">→</span>
            </button>
          </div>
        </div>

        <!-- PRIVACY -->
        <div class="section">
          <div class="sectionLabel">PRIVACY</div>

          <div class="card">
            <button class="row" (click)="toggleBlocks()">
              <span class="rowLabel">Blocked users</span>
              <div class="rowRight">
                <span class="rowCount" *ngIf="blockedUsers.length > 0">{{ blockedUsers.length }}</span>
                <span class="rowArrow" [class.open]="showBlocks">{{ showBlocks ? '↑' : '↓' }}</span>
              </div>
            </button>

            <div class="blockList" *ngIf="showBlocks">
              <div class="blockEmpty" *ngIf="blockedUsers.length === 0 && !loadingBlocks">
                No blocked users
              </div>
              <div class="blockEmpty" *ngIf="loadingBlocks">Loading…</div>
              <div class="blockRow" *ngFor="let u of blockedUsers">
                <div class="blockAvatar">
                  <img *ngIf="u.photo" [src]="u.photo" [alt]="u.name" />
                  <span *ngIf="!u.photo" class="blockInitial">{{ u.name[0] }}</span>
                </div>
                <span class="blockName">{{ u.name }}</span>
                <button class="unblockBtn" (click)="unblock(u)" [disabled]="unblocking === u.userId">
                  {{ unblocking === u.userId ? '…' : 'Unblock' }}
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- NOTIFICATIONS -->
        <div class="section">
          <div class="sectionLabel">NOTIFICATIONS</div>

          <div class="card">
            <div class="row noTap">
              <span class="rowLabel">Push notifications</span>
              <button class="toggle" [class.on]="notifEnabled" [disabled]="notifPending || !notifSupported"
                      (click)="toggleNotif()" [style.opacity]="notifPending ? '0.5' : '1'">
                <span class="thumb"></span>
              </button>
            </div>
          </div>
        </div>

        <!-- FOR YOU -->
        <div class="section">
          <div class="sectionLabel">FOR YOU</div>

          <div class="card">
            <div class="row noTap">
              <span class="rowLabel">Weekly reflection</span>
              <button class="toggle" [class.on]="coachingEnabled" [disabled]="coachingPending"
                      (click)="toggleCoaching()" [style.opacity]="coachingPending ? '0.5' : '1'">
                <span class="thumb"></span>
              </button>
            </div>
          </div>
        </div>

        <!-- ABOUT -->
        <div class="section">
          <div class="sectionLabel">ABOUT</div>
          <div class="card">
            <a class="row link" routerLink="/privacy">
              <span class="rowLabel">Privacy Policy</span>
              <span class="rowArrow">→</span>
            </a>
            <div class="divider"></div>
            <a class="row link" routerLink="/terms">
              <span class="rowLabel">Terms of Service</span>
              <span class="rowArrow">→</span>
            </a>
          </div>
        </div>

        <div class="version">Woven v0.1</div>
      </div>

      <!-- Delete confirmation overlay -->
      <div class="overlay" *ngIf="deleteStep > 0" (click)="cancelDelete()">
        <div class="sheet" (click)="$event.stopPropagation()">

          <div *ngIf="deleteStep === 1">
            <div class="sheetTitle">Delete your account?</div>
            <div class="sheetBody">
              This permanently removes your profile, photos, tiles, and chat history.
              Matches will be anonymized. This cannot be undone.
            </div>
            <div class="sheetActions">
              <button class="sheetCancel" (click)="cancelDelete()">Cancel</button>
              <button class="sheetDanger" (click)="deleteStep = 2; cdr.markForCheck()">
                Yes, delete
              </button>
            </div>
          </div>

          <div *ngIf="deleteStep === 2">
            <div class="sheetTitle">Are you absolutely sure?</div>
            <div class="sheetBody">
              Once deleted, your account is gone forever. There is no recovery.
            </div>
            <div class="sheetActions">
              <button class="sheetCancel" (click)="cancelDelete()">Cancel</button>
              <button class="sheetDanger" [disabled]="deleting" (click)="confirmDelete()">
                {{ deleting ? 'Deleting…' : 'Delete forever' }}
              </button>
            </div>
          </div>

        </div>
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
    }

    /* ── Header ── */
    .topBar {
      position: sticky;
      top: 0;
      z-index: 10;
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 52px 20px 16px;
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

    /* ── Content ── */
    .content {
      padding: 24px 16px 48px;
      max-width: 480px;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      gap: 28px;
    }

    /* ── Section ── */
    .section { display: flex; flex-direction: column; gap: 8px; }
    .sectionLabel {
      font-size: 10px;
      font-weight: 700;
      letter-spacing: 0.12em;
      color: var(--text-muted);
      padding-left: 4px;
    }

    /* ── Card ── */
    .card {
      background: var(--bg-surface);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      overflow: hidden;
    }

    /* ── Row ── */
    .row {
      width: 100%;
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 18px;
      background: transparent;
      border: none;
      color: var(--text-primary);
      font-family: var(--font-ui);
      font-size: var(--text-sm);
      font-weight: 500;
      cursor: pointer;
      text-align: left;
      text-decoration: none;
    }
    .row:active { background: rgba(255,255,255,0.04); }
    .row.noTap { cursor: default; }
    .row.danger .rowLabel { color: #E8564A; }
    .row.link { display: flex; }

    .rowLabel { flex: 1; }
    .rowRight { display: flex; align-items: center; gap: 8px; }
    .rowArrow { color: var(--text-muted); font-size: 14px; transition: transform 150ms; }
    .rowArrow.open { display: inline-block; }
    .rowCount {
      font-size: 11px;
      font-weight: 700;
      color: var(--text-muted);
      background: var(--bg-elevated);
      border-radius: var(--radius-full);
      padding: 2px 8px;
    }
    .divider { height: 1px; background: var(--border-subtle); margin: 0 18px; }

    /* ── Toggle ── */
    .toggle {
      position: relative;
      width: 44px; height: 26px;
      border-radius: var(--radius-full);
      background: var(--bg-elevated);
      border: 1px solid var(--border-soft);
      cursor: pointer;
      transition: background 200ms;
      padding: 0;
    }
    .toggle.on { background: var(--rose-400); border-color: var(--rose-400); }
    .thumb {
      position: absolute;
      top: 3px; left: 3px;
      width: 18px; height: 18px;
      border-radius: 50%;
      background: #fff;
      transition: transform 200ms var(--ease-out);
      box-shadow: 0 1px 4px rgba(0,0,0,0.4);
    }
    .toggle.on .thumb { transform: translateX(18px); }

    /* ── Block list ── */
    .blockList {
      border-top: 1px solid var(--border-subtle);
      padding: 8px 0;
    }
    .blockEmpty {
      padding: 12px 18px;
      font-size: var(--text-sm);
      color: var(--text-muted);
    }
    .blockRow {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 18px;
    }
    .blockAvatar {
      width: 36px; height: 36px;
      border-radius: 50%;
      background: var(--bg-elevated);
      overflow: hidden;
      flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
    }
    .blockAvatar img { width: 100%; height: 100%; object-fit: cover; }
    .blockInitial { font-size: 15px; font-weight: 700; color: var(--text-muted); }
    .blockName { flex: 1; font-size: var(--text-sm); font-weight: 500; }
    .unblockBtn {
      font-size: 12px;
      font-weight: 600;
      font-family: var(--font-ui);
      color: var(--plum-300);
      background: rgba(159, 153, 232, 0.10);
      border: 1px solid rgba(159, 153, 232, 0.20);
      border-radius: var(--radius-md);
      padding: 5px 12px;
      cursor: pointer;
    }
    .unblockBtn:disabled { opacity: 0.5; cursor: default; }

    /* ── Version ── */
    .version {
      text-align: center;
      font-size: 11px;
      color: var(--text-dim);
      letter-spacing: 0.04em;
    }

    /* ── Delete overlay ── */
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
      padding: 28px 24px 48px;
    }
    .sheetTitle {
      font-family: var(--font-display);
      font-size: var(--text-xl);
      font-weight: 700;
      margin-bottom: 12px;
    }
    .sheetBody {
      font-size: var(--text-sm);
      color: var(--text-secondary);
      line-height: 1.6;
      margin-bottom: 24px;
    }
    .sheetActions {
      display: flex;
      gap: 12px;
    }
    .sheetCancel, .sheetDanger {
      flex: 1;
      height: 48px;
      border-radius: var(--radius-md);
      font-family: var(--font-ui);
      font-size: var(--text-sm);
      font-weight: 700;
      cursor: pointer;
      border: none;
    }
    .sheetCancel {
      background: var(--bg-elevated);
      color: var(--text-primary);
    }
    .sheetDanger {
      background: #C0392B;
      color: #fff;
    }
    .sheetDanger:disabled { opacity: 0.6; cursor: default; }
  `]
})
export class SettingsPageComponent implements OnInit {
  blockedUsers: BlockedUser[] = [];
  showBlocks = false;
  loadingBlocks = false;
  unblocking: number | null = null;

  notifEnabled = false;
  notifPending = false;
  notifSupported = true;

  coachingEnabled = true;
  coachingPending = false;

  deleteStep = 0;
  deleting = false;

  private api = environment.apiUrl;

  constructor(
    private router: Router,
    private http: HttpClient,
    public cdr: ChangeDetectorRef,
    private push: PushService,
    private coachingSvc: CoachingService,
  ) {}

  async ngOnInit() {
    this.notifSupported = await this.push.isSupported();
    this.notifEnabled = this.notifSupported && await this.push.isSubscribed();
    this.cdr.markForCheck();
  }

  back() { this.router.navigateByUrl('/you'); }

  logout() {
    localStorage.removeItem('accessToken');
    localStorage.removeItem('user');
    this.router.navigateByUrl('/login');
  }

  toggleBlocks() {
    this.showBlocks = !this.showBlocks;
    if (this.showBlocks && this.blockedUsers.length === 0 && !this.loadingBlocks) {
      this.fetchBlocks();
    }
    this.cdr.markForCheck();
  }

  private fetchBlocks() {
    this.loadingBlocks = true;
    const token = localStorage.getItem('accessToken') ?? '';
    this.http.get<BlockedUser[]>(`${this.api}/me/blocks`, {
      headers: { Authorization: `Bearer ${token}` }
    }).subscribe({
      next: users => {
        this.blockedUsers = users;
        this.loadingBlocks = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loadingBlocks = false;
        this.cdr.markForCheck();
      }
    });
  }

  unblock(user: BlockedUser) {
    this.unblocking = user.userId;
    const token = localStorage.getItem('accessToken') ?? '';
    this.http.delete(`${this.api}/me/blocks/${user.userId}`, {
      headers: { Authorization: `Bearer ${token}` }
    }).subscribe({
      next: () => {
        this.blockedUsers = this.blockedUsers.filter(u => u.userId !== user.userId);
        this.unblocking = null;
        this.cdr.markForCheck();
      },
      error: () => {
        this.unblocking = null;
        this.cdr.markForCheck();
      }
    });
  }

  async toggleCoaching() {
    if (this.coachingPending) return;
    this.coachingPending = true;
    this.cdr.markForCheck();
    try {
      if (this.coachingEnabled) {
        await firstValueFrom(this.coachingSvc.optOut());
        this.coachingEnabled = false;
      } else {
        await firstValueFrom(this.coachingSvc.optIn());
        this.coachingEnabled = true;
      }
    } catch { /* non-critical */ }
    finally {
      this.coachingPending = false;
      this.cdr.markForCheck();
    }
  }

  async toggleNotif() {
    if (this.notifPending || !this.notifSupported) return;
    this.notifPending = true;
    this.cdr.markForCheck();
    try {
      if (this.notifEnabled) {
        await this.push.unregister();
        this.notifEnabled = false;
      } else {
        this.notifEnabled = await this.push.register();
      }
    } finally {
      this.notifPending = false;
      this.cdr.markForCheck();
    }
  }

  startDelete() {
    this.deleteStep = 1;
    this.cdr.markForCheck();
  }

  cancelDelete() {
    this.deleteStep = 0;
    this.cdr.markForCheck();
  }

  confirmDelete() {
    this.deleting = true;
    const token = localStorage.getItem('accessToken') ?? '';
    this.http.delete(`${this.api}/me/account`, {
      headers: { Authorization: `Bearer ${token}` }
    }).subscribe({
      next: () => {
        localStorage.removeItem('accessToken');
        localStorage.removeItem('user');
        this.router.navigateByUrl('/login');
      },
      error: () => {
        this.deleting = false;
        this.deleteStep = 0;
        this.cdr.markForCheck();
      }
    });
  }
}
