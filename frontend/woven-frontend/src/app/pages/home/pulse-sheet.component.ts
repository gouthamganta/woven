import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { PulseAnswers, PulseQuestion, PulseState } from '../../services/pulse.service';

@Component({
  selector: 'app-pulse-sheet',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="overlay" (click)="close()">
      <div class="sheet" (click)="$event.stopPropagation()">
        <div class="grab"></div>

        <div class="header">
          <div class="left">
            <div class="kicker">
              <span class="kDot" [class.off]="!state?.answered"></span>
              Pulse
            </div>

            <div class="title">Quick check-in</div>
            <div class="subtitle">{{ cycleText }}</div>
          </div>

          <button class="close" (click)="close()" aria-label="Close">×</button>
        </div>

        <div class="body" *ngIf="state">
          <div class="question" *ngFor="let q of state.questions">
            <div class="qTitle">{{ q.text }}</div>

            <div class="grid">
              <button
                class="card"
                *ngFor="let opt of q.options"
                [class.selected]="isSelected(q.id, opt.key)"
                [disabled]="readonly"
                [ngClass]="gradeFor(q.id, opt.key)"
                (click)="pick(q.id, opt.key)"
              >
                <div class="cardLabel">{{ opt.label }}</div>
                <div class="cardSub" *ngIf="opt.subLabel">{{ opt.subLabel }}</div>
              </button>
            </div>
          </div>
        </div>

        <div class="footer">
          <div class="note" *ngIf="readonly">
            Already checked in for this cycle.
            <span class="dim">You can update after reset.</span>
          </div>

          <div class="note" *ngIf="!readonly && !complete">
            Pick 1 option in each section.
            <span class="dim">Takes ~10 seconds.</span>
          </div>

          <div class="actions">
            <button class="btn ghost" (click)="close()">Not now</button>
            <button class="btn primary" [disabled]="readonly || !complete" (click)="save()">
              Save
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .overlay{
      position: fixed; inset: 0;
      background: rgba(0,0,0,0.34);
      backdrop-filter: blur(10px);
      display:flex;
      align-items:flex-end;
      justify-content:center;
      padding: 14px;
      z-index: 2000;
    }

    /* ✅ Not full-screen wide */
    .sheet{
      width: min(540px, 100%);
      max-height: calc(100vh - 18px);
      background: rgba(255,255,255,0.96);
      border: 1px solid rgba(0,0,0,0.10);
      border-radius: 22px;
      box-shadow: 0 28px 85px rgba(0,0,0,0.22);
      overflow:hidden;
      animation: rise .18s ease-out;
    }

    @keyframes rise {
      from { transform: translateY(14px); opacity: .88; }
      to { transform: translateY(0); opacity: 1; }
    }

    .grab{
      width: 54px; height: 5px;
      border-radius: 999px;
      background: rgba(0,0,0,0.16);
      margin: 10px auto 6px;
    }

    .header{
      display:flex;
      justify-content:space-between;
      align-items:flex-start;
      gap: 12px;
      padding: 10px 14px 12px;
      border-bottom: 1px solid rgba(0,0,0,0.08);
    }

    .kicker{
      display:flex;
      align-items:center;
      gap: 8px;
      font-size: 11px;
      letter-spacing: .22em;
      font-weight: 900;
      opacity: .72;
      text-transform: uppercase;
    }

    .kDot{
      width: 8px; height: 8px;
      border-radius: 999px;
      background: rgba(0,0,0,0.75);
    }
    .kDot.off{ background: rgba(0,0,0,0.25); }

    .title{
      margin-top: 6px;
      font-size: 18px;
      font-weight: 950;
      letter-spacing: -0.02em;
      line-height: 1.1;
    }

    .subtitle{
      margin-top: 6px;
      font-size: 12px;
      opacity: .68;
      line-height: 1.25;
    }

    .close{
      width: 40px;
      height: 36px;
      border-radius: 14px;
      border: 1px solid rgba(0,0,0,0.10);
      background: rgba(0,0,0,0.05);
      cursor:pointer;
      flex: 0 0 auto;
      font-size: 18px;
      font-weight: 900;
    }

    .body{
      padding: 10px 14px 8px;
      overflow:auto;
      max-height: 58vh;
    }

    .question{ margin-bottom: 14px; }

    .qTitle{
      font-size: 12.8px;
      font-weight: 950;
      letter-spacing: -0.01em;
      margin: 10px 2px 10px;
      opacity: .92;
    }

    /* ✅ compact + consistent */
    .grid{
      display:grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 10px;
    }

    .card{
      text-align:left;
      border-radius: 18px;
      border: 1px solid rgba(0,0,0,0.10);
      padding: 12px 11px;
      cursor:pointer;
      transition: transform .10s ease, box-shadow .12s ease, border-color .12s ease, background .12s ease;
      box-shadow: 0 10px 18px rgba(0,0,0,0.06);
      min-height: 92px;
      overflow:hidden;
    }

    .card:active{ transform: scale(0.985); }
    .card:disabled{ opacity: .62; cursor:not-allowed; transform:none; }

    .card.selected{
      border-color: rgba(0,0,0,0.34);
      box-shadow: 0 14px 24px rgba(0,0,0,0.10);
    }

    .cardLabel{
      font-size: 13px;
      font-weight: 950;
      letter-spacing: -0.01em;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    /* ✅ keep descriptions subtle, not wide */
    .cardSub{
      margin-top: 6px;
      font-size: 11px;
      opacity: .65;
      line-height: 1.18;
      display: block;
      overflow: visible;
      white-space: normal;
    }

    /* ✅ Dark/Mid/Light graded (no icons, no pills) */
    .g-dark{ background: rgba(0,0,0,0.10); }
    .g-mid{ background: rgba(0,0,0,0.06); }
    .g-light{ background: rgba(0,0,0,0.03); }

    .footer{
      padding: 12px 14px 14px;
      border-top: 1px solid rgba(0,0,0,0.08);
      background: rgba(255,255,255,0.92);
      display:flex;
      justify-content:space-between;
      align-items:center;
      gap: 12px;
      flex-wrap: wrap;
    }

    .note{
      font-size: 12px;
      opacity: .78;
    }
    .dim{ opacity: .7; }

    .actions{
      display:flex;
      gap: 10px;
      margin-left: auto;
    }

    .btn{
      border:none;
      border-radius: 14px;
      padding: 12px 14px;
      font-weight: 900;
      cursor:pointer;
      min-width: 120px;
    }

    .btn.ghost{
      background: rgba(0,0,0,0.06);
      border: 1px solid rgba(0,0,0,0.10);
      color:#111;
    }

    .btn.primary{
      background:#111;
      color:#fff;
    }

    .btn:disabled{ opacity:.55; cursor:not-allowed; }

    @media (max-width: 560px){
      .grid{ grid-template-columns: 1fr; }
      .sheet{ width: 100%; }
    }
  `]
})
export class PulseSheetComponent {
  @Input() state: PulseState | null = null;
  @Input() readonly = false;

  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<PulseAnswers>();

  draft: Partial<PulseAnswers> = {};

  ngOnChanges() {
    this.draft = { ...(this.state?.answers ?? {}) };
  }

  close(){ this.closed.emit(); }

  get complete(){
    return !!(this.draft.d1_battery && this.draft.d2_tone && this.draft.d3_role);
  }

  get cycleText(){
    if (!this.state) return 'Updates every 48 hours.';
    const end = new Date(this.state.cycleEndUtc);
    return `Updates every 48 hours • resets ${end.toLocaleString()}`;
  }

  isSelected(qid: PulseQuestion['id'], key: string){
    if (qid === 'd1_battery') return this.draft.d1_battery === key;
    if (qid === 'd2_tone') return this.draft.d2_tone === key;
    return this.draft.d3_role === key;
  }

  pick(qid: PulseQuestion['id'], key: string){
    if (this.readonly) return;
    if (qid === 'd1_battery') this.draft.d1_battery = key as any;
    if (qid === 'd2_tone') this.draft.d2_tone = key as any;
    if (qid === 'd3_role') this.draft.d3_role = key as any;
  }

  save(){
    if (!this.complete) return;
    this.saved.emit(this.draft as PulseAnswers);
  }

  /* ✅ graded style mapping */
  gradeFor(qid: PulseQuestion['id'], key: string){
    if (qid === 'd1_battery') {
      if (key === 'high') return 'g-dark';
      if (key === 'medium') return 'g-mid';
      return 'g-light';
    }

    if (qid === 'd2_tone') {
      if (key === 'playful') return 'g-dark';
      if (key === 'serious') return 'g-mid';
      return 'g-light'; // calm
    }

    // role
    if (key === 'driver') return 'g-dark';
    if (key === 'copilot') return 'g-mid';
    return 'g-light';
  }
}
