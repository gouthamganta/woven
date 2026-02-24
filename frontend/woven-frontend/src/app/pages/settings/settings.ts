import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="top">
        <button class="back" (click)="back()">←</button>
        <div class="brand">WOVEN</div>
        <div class="meta">Settings</div>
      </div>

      <div class="watermark">W</div>

      <div class="content">
        <div class="panel">
          <h2 class="title">Settings</h2>

          <div class="row">
            <div>
              <div class="label">Notifications</div>
              <div class="hint">UI only for now</div>
            </div>
            <input type="checkbox" />
          </div>

          <div class="row">
            <div>
              <div class="label">Logout</div>
              <div class="hint">Hook backend later</div>
            </div>
            <button class="danger" (click)="logout()">Logout</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page{ min-height:100vh; position:relative; overflow:hidden; padding: 86px 16px 16px; }
    .top{ position:fixed; top:20px; left:22px; right:22px; display:grid; grid-template-columns: 44px 1fr auto; align-items:center; z-index:5; }
    .back{ width:40px; height:32px; border:none; border-radius:12px; background:rgba(255,255,255,0.7); border:1px solid rgba(0,0,0,.08); }
    .brand{ letter-spacing:.22em; font-weight:750; font-size:12px; text-transform:uppercase; justify-self:center; opacity:.92; }
    .meta{ font-size:12px; opacity:.65; justify-self:end; }
    .watermark{ position:absolute; right:-40px; top:-30px; font-size:240px; font-weight:800; letter-spacing:-0.06em; color:rgba(0,0,0,0.035); transform:rotate(-8deg); user-select:none; pointer-events:none; z-index:0; }
    .content{ max-width:760px; margin:0 auto; position:relative; z-index:2; }
    .panel{ background:rgba(255,255,255,0.92); border:1px solid rgba(0,0,0,.08); border-radius:20px; box-shadow:0 16px 40px rgba(0,0,0,.08); padding:18px; }
    .title{ margin:0 0 12px; font-size:20px; }
    .row{ display:flex; justify-content:space-between; align-items:center; padding:12px 0; border-top:1px solid rgba(0,0,0,.07); }
    .row:first-of-type{ border-top:none; }
    .label{ font-weight:700; font-size:13px; }
    .hint{ font-size:12px; opacity:.65; margin-top:2px; }
    .danger{ border:none; padding:10px 12px; border-radius:12px; background:rgba(0,0,0,0.06); }
  `]
})
export class SettingsPageComponent {
  constructor(private router: Router) {}
  back(){ this.router.navigateByUrl('/home/profile'); }
  logout(){
    if (typeof window !== 'undefined') {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('user');
    }
    this.router.navigateByUrl('/login');
  }
}
