import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="page">
      <div class="top">
        <div class="brand">WOVEN</div>
        <div class="meta">Profile</div>
      </div>

      <div class="watermark">W</div>

      <div class="content">
        <div class="panel">
          <h2 class="title">Your profile</h2>
          <p class="sub">Edit your profile or manage settings.</p>

          <button class="btn" (click)="goEdit()">Edit Profile</button>
          <button class="btn ghost" (click)="goSettings()">Settings</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page{ min-height:100vh; position:relative; overflow:hidden; padding: 86px 16px 16px; }
    .top{ position:fixed; top:20px; left:22px; right:22px; display:flex; justify-content:space-between; align-items:center; opacity:.92; z-index:5; pointer-events:none; }
    .brand{ letter-spacing:.22em; font-weight:750; font-size:12px; text-transform:uppercase; }
    .meta{ font-size:12px; opacity:.65; }
    .watermark{ position:absolute; right:-40px; top:-30px; font-size:240px; font-weight:800; letter-spacing:-0.06em; color:rgba(0,0,0,0.035); transform:rotate(-8deg); user-select:none; pointer-events:none; z-index:0; }
    .content{ max-width:760px; margin:0 auto; position:relative; z-index:2; }
    .panel{ background:rgba(255,255,255,0.92); border:1px solid rgba(0,0,0,.08); border-radius:20px; box-shadow:0 16px 40px rgba(0,0,0,.08); padding:18px; }
    .title{ margin:0 0 6px; font-size:20px; letter-spacing:-0.01em; }
    .sub{ margin:0 0 14px; font-size:13px; opacity:.7; }
    .btn{ width:100%; border:none; border-radius:14px; padding:12px 12px; font-weight:650; background:#111; color:#fff; margin-top:10px; }
    .btn.ghost{ background:rgba(0,0,0,0.06); color:#111; }
  `]
})
export class ProfilePageComponent {
  constructor(private router: Router) {}
  goEdit(){ this.router.navigateByUrl('/onboarding/review'); }
  goSettings(){ this.router.navigateByUrl('/settings'); }
}
