import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { OnboardingService, ReviewResponse } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

type Tab = 'self' | 'public';

type PhotoView = { url: string; caption?: string; sortOrder: number };
type PublicOptionalField = { key: string; value: string };

// ✅ Foundational UI type (what backend returns under self.foundational.qa)
type FoundationalQa = { id: string; q: string; a: string };

@Component({
  standalone: true,
  imports: [CommonModule, OnboardingShellComponent],
  template: `
    <woven-onboarding-shell
      title="Review"
      subtitle="Preview your profile in two modes. You can edit anything."
      [stepNumber]="6"
      [totalSteps]="6"
      stepLabel="Profile"
    >
      <style>
        /* ===========================
           BASE / TYPOGRAPHY (refined)
        =========================== */
        .ui, .ui * {
          font-family: ui-sans-serif, system-ui, -apple-system, "SF Pro Display", "Segoe UI", Roboto, Helvetica, Arial;
          letter-spacing: 0;
          font-weight: 500;
        }

        .wrap { max-width: 980px; margin: 0 auto; }

        .topRow {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 12px;
          margin: 10px 0 14px;
        }

        .tabs {
          display: inline-flex;
          padding: 6px;
          border-radius: 999px;
          border: 1px solid rgba(0,0,0,.10);
          background: rgba(255,255,255,.82);
          box-shadow: 0 8px 30px rgba(0,0,0,.06);
          gap: 6px;
        }

        .tabBtn {
          appearance: none;
          border: 0;
          cursor: pointer;
          padding: 10px 14px;
          border-radius: 999px;
          background: transparent;
          font-size: 13px;
          font-weight: 650;
          color: rgba(12,18,28,.72);
          transition: .18s ease;
        }

        .tabBtn.active {
          background: rgba(0,0,0,.90);
          color: #fff;
          box-shadow: 0 10px 30px rgba(0,0,0,.14);
        }

        .smallHint {
          font-size: 12px;
          opacity: .65;
          line-height: 1.35;
        }

        .stateLine { padding: 10px 0; font-size: 13px; opacity: .70; }

        .error {
          margin-top: 10px;
          padding: 12px 14px;
          border-radius: 14px;
          background: rgba(176,0,32,.06);
          border: 1px solid rgba(176,0,32,.18);
          color: #b00020;
          font-size: 13px;
        }

        /* ===========================
           BUTTONS
        =========================== */
        .btnRow { display: flex; gap: 10px; align-items: center; }

        .btn {
          appearance: none;
          border: 0;
          cursor: pointer;
          padding: 12px 14px;
          border-radius: 14px;
          font-size: 13px;
          font-weight: 700;
          transition: .18s ease;
        }

        .btn.primary {
          background: rgba(0,0,0,.92);
          color: #fff;
          box-shadow: 0 12px 34px rgba(0,0,0,.18);
        }
        .btn.primary:hover { transform: translateY(-1px); }

        .btn.ghost {
          background: rgba(255,255,255,.85);
          border: 1px solid rgba(0,0,0,.12);
          color: rgba(12,18,28,.82);
          box-shadow: 0 10px 26px rgba(0,0,0,.06);
        }
        .btn.ghost:hover { transform: translateY(-1px); }

        .btn:disabled { opacity: .55; cursor: not-allowed; transform: none !important; }

        .linkBtn {
          appearance: none;
          border: 0;
          cursor: pointer;
          background: transparent;
          color: #0b66ff;
          font-weight: 750;
          font-size: 13px;
          padding: 8px 10px;
          border-radius: 12px;
        }
        .linkBtn:hover { background: rgba(11,102,255,.08); }

        /* ===========================
           SELF TAB
        =========================== */
        .section { padding: 18px 0; border-top: 1px solid rgba(0,0,0,.06); }
        .section:first-of-type { border-top: none; }

        .sectionHead { display: flex; align-items: flex-start; justify-content: space-between; gap: 14px; }

        .hTitle { font-size: 14px; font-weight: 800; color: rgba(12,18,28,.92); letter-spacing: -.2px; }
        .hSub { margin-top: 6px; font-size: 12px; opacity: .60; line-height: 1.35; }

        .line { margin-top: 10px; font-size: 14px; line-height: 1.45; color: rgba(12,18,28,.88); }

        .cardsGrid {
          margin-top: 12px;
          display: grid;
          grid-template-columns: repeat(3, minmax(0, 1fr));
          gap: 12px;
        }
        @media (max-width: 900px) {
          .cardsGrid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
        }

        .imgCard {
          border-radius: 18px;
          overflow: hidden;
          background: #fff;
          border: 1px solid rgba(0,0,0,.10);
          box-shadow: 0 12px 30px rgba(0,0,0,.06);
        }
        .imgCard img { width: 100%; height: 180px; object-fit: cover; display: block; }
        .imgMeta { padding: 10px 12px; font-size: 12px; opacity: .70; line-height: 1.35; }

        .block {
          margin-top: 12px;
          padding: 14px;
          border-radius: 18px;
          background: rgba(255,255,255,.92);
          border: 1px solid rgba(0,0,0,.10);
          box-shadow: 0 12px 30px rgba(0,0,0,.06);
        }
        .blockLabel {
          font-size: 11px;
          font-weight: 800;
          letter-spacing: .8px;
          text-transform: uppercase;
          opacity: .60;
          margin-bottom: 8px;
        }
        .blockText {
          font-size: 14px;
          line-height: 1.5;
          color: rgba(12,18,28,.88);
          white-space: pre-wrap;
        }

        .kv { display: grid; gap: 10px; }
        .kvRow {
          display: grid;
          grid-template-columns: 1fr 1.3fr;
          gap: 12px;
          align-items: baseline;
          padding-top: 8px;
          border-top: 1px dashed rgba(0,0,0,.10);
        }
        .kvRow:first-child { border-top: none; padding-top: 0; }
        .k {
          font-size: 12px;
          opacity: .60;
          font-weight: 750;
          text-transform: uppercase;
          letter-spacing: .4px;
        }
        .v { font-size: 14px; color: rgba(12,18,28,.88); }

        /* ✅ Foundational Q/A styling */
        .qaList { display: grid; gap: 12px; margin-top: 12px; }
        .qaItem {
          padding: 14px;
          border-radius: 18px;
          background: rgba(255,255,255,.92);
          border: 1px solid rgba(0,0,0,.10);
          box-shadow: 0 12px 30px rgba(0,0,0,.06);
        }
        .qaQ {
          font-size: 12px;
          font-weight: 800;
          text-transform: uppercase;
          letter-spacing: .7px;
          opacity: .62;
          margin-bottom: 8px;
        }
        .qaA {
          font-size: 14px;
          line-height: 1.5;
          color: rgba(12,18,28,.88);
          white-space: pre-wrap;
        }

        /* ===========================
           PUBLIC TAB (refined)
        =========================== */
        .publicFrame {
          border-radius: 26px;
          overflow: hidden;
          border: 1px solid rgba(0,0,0,.10);
          background: #fff;
          box-shadow: 0 24px 90px rgba(0,0,0,.14);
        }

        .feed {
          height: 78vh;
          overflow-y: auto;
          scroll-snap-type: y mandatory;
          scroll-behavior: smooth;
          -webkit-overflow-scrolling: touch;
          background: #fff;
        }
        .feed::-webkit-scrollbar { width: 0px; }

        .feedItem {
          scroll-snap-align: start;
          min-height: 78vh;
          display: grid;
          grid-template-rows: 1fr auto;
          background: #fff;
        }

        .photoStage { position: relative; background: rgba(0,0,0,.05); }
        .heroImg { width: 100%; height: 100%; object-fit: cover; display: block; }

        .heroTag {
          position: absolute;
          left: 14px;
          bottom: 14px;
          padding: 10px 12px;
          border-radius: 16px;
          background: rgba(0,0,0,.36);
          border: 1px solid rgba(255,255,255,.18);
          color: #fff;
          box-shadow: 0 16px 42px rgba(0,0,0,.22);
          max-width: 92%;
        }
        .heroTag .name {
          font-size: 22px;
          font-weight: 650;
          letter-spacing: -.2px;
          line-height: 1.05;
        }

        .capBtn {
          position: absolute;
          left: 12px;
          top: 12px;
          appearance: none;
          border: 1px solid rgba(255,255,255,.28);
          background: rgba(0,0,0,.55);
          color: #fff;
          cursor: pointer;
          border-radius: 999px;
          padding: 8px 10px;
          font-size: 12px;
          font-weight: 650;
        }

        .capPop {
          position: absolute;
          left: 12px;
          top: 52px;
          max-width: 90%;
          padding: 10px 12px;
          border-radius: 14px;
          border: 1px solid rgba(255,255,255,.22);
          background: rgba(0,0,0,.70);
          color: #fff;
          font-size: 13px;
          line-height: 1.35;
          font-weight: 520;
          box-shadow: 0 18px 60px rgba(0,0,0,.30);
          white-space: pre-wrap;
        }

        .card {
          border-top: 1px solid rgba(0,0,0,.10);
          background: rgba(255,255,255,.96);
          padding: 14px 14px 16px;
          display: grid;
          gap: 10px;
        }

        .cardTitle {
          font-size: 11px;
          font-weight: 800;
          letter-spacing: .8px;
          text-transform: uppercase;
          opacity: .60;
        }

        .cardBody {
          font-size: 15px;
          line-height: 1.55;
          color: rgba(12,18,28,.88);
          white-space: pre-wrap;
        }

        .chipsRow {
          display:flex;
          gap: 8px;
          overflow-x: auto;
          padding-bottom: 2px;
          -webkit-overflow-scrolling: touch;
        }
        .chipsRow::-webkit-scrollbar { height: 6px; }
        .chipsRow::-webkit-scrollbar-thumb { background: rgba(0,0,0,.16); border-radius: 999px; }

        .chip {
          white-space: nowrap;
          border: 1px solid rgba(0,0,0,.10);
          background: rgba(0,0,0,.03);
          padding: 7px 10px;
          border-radius: 999px;
          font-size: 12px;
          color: rgba(12,18,28,.78);
        }
        .chip b { font-weight: 750; color: rgba(12,18,28,.88); }

        .noTitleSpacer { height: 2px; }
      </style>

      <div class="ui wrap">
        <div class="topRow">
          <div>
            <div class="tabs">
              <button class="tabBtn" [class.active]="tab==='self'" (click)="tab='self'">What we saved</button>
              <button class="tabBtn" [class.active]="tab==='public'" (click)="tab='public'">What others see</button>
            </div>
            <div class="smallHint">Tip: Use the public preview to see the “swipe” experience.</div>
          </div>

          <div class="btnRow" *ngIf="tab==='self'">
            <button class="btn ghost" (click)="reload()" [disabled]="loading || completing">Refresh</button>
            <button class="btn primary" (click)="complete()" [disabled]="loading || completing">
              {{ completing ? 'Finishing…' : 'Finish' }}
            </button>
          </div>
        </div>

        <div *ngIf="loading" class="stateLine">Loading…</div>
        <div *ngIf="error" class="error">{{ error }}</div>

        <ng-container *ngIf="!loading && data">

          <!-- ===========================
               PUBLIC TAB
          =========================== -->
          <ng-container *ngIf="tab==='public'">
            <div class="publicFrame">
              <div class="feed">
                <ng-container *ngIf="publicPhotos.length > 0; else noPhotos">

                  <section class="feedItem" *ngFor="let p of publicPhotos; let idx = index">
                    <div class="photoStage">
                      <img class="heroImg" [src]="p.url" [alt]="'Profile photo ' + (idx+1)" />

                      <button class="capBtn" (click)="toggleCaption(idx, p.caption); $event.stopPropagation()">
                        Caption
                      </button>

                      <div class="capPop" *ngIf="captionOpenIndex === idx">
                        {{ captionText || 'No caption added.' }}
                      </div>

                      <div class="heroTag" *ngIf="idx===0">
                        <div class="name">
                          {{ publicName }}<span *ngIf="publicAge">, {{ publicAge }}</span>
                        </div>
                      </div>
                    </div>

                    <div class="card">

                      <ng-container *ngIf="idx===0">
                        <div class="cardTitle">Basics</div>
                        <div class="chipsRow" *ngIf="publicBasicsChips.length">
                          <div class="chip" *ngFor="let c of publicBasicsChips">{{ c }}</div>
                        </div>
                        <div class="cardBody" *ngIf="publicIntentOpennessLine">{{ publicIntentOpennessLine }}</div>
                      </ng-container>

                      <ng-container *ngIf="idx===1 && publicWeeklyVibe">
                        <div class="cardTitle">Weekly vibe</div>
                        <div class="cardBody">{{ publicWeeklyVibe }}</div>
                      </ng-container>

                      <ng-container *ngIf="idx===2">
                        <ng-container *ngIf="publicOptional.length">
                          <div class="noTitleSpacer"></div>
                          <div class="chipsRow">
                            <div class="chip" *ngFor="let f of publicOptional">
                              <b>{{ prettyKey(f.key) }}</b>&nbsp;{{ f.value }}
                            </div>
                          </div>
                        </ng-container>

                        <ng-container *ngIf="publicBio">
                          <div style="height: 10px;"></div>
                          <div class="cardTitle">Bio</div>
                          <div class="cardBody">{{ publicBio }}</div>
                        </ng-container>
                      </ng-container>

                      <ng-container *ngIf="idx>2">
                        <div class="cardBody" style="opacity:.55;">&nbsp;</div>
                      </ng-container>

                    </div>
                  </section>

                </ng-container>

                <ng-template #noPhotos>
                  <div style="padding: 22px; opacity:.75;">No photos yet.</div>
                </ng-template>
              </div>
            </div>
          </ng-container>

          <!-- ===========================
               SELF TAB
          =========================== -->
          <ng-container *ngIf="tab==='self'">

            <section class="section">
              <div class="sectionHead">
                <div>
                  <div class="hTitle">Basics</div>
                  <div class="hSub">Your core profile info.</div>
                </div>
                <button class="linkBtn" (click)="go('/onboarding/basics')">Edit</button>
              </div>
              <div class="line">{{ selfBasicsLine }}</div>
              <div class="line" *ngIf="selfDatingPrefsLine" style="opacity:.75;">{{ selfDatingPrefsLine }}</div>
            </section>

            <section class="section">
              <div class="sectionHead">
                <div>
                  <div class="hTitle">Photos</div>
                  <div class="hSub">{{ selfPhotos.length }} added.</div>
                </div>
                <button class="linkBtn" (click)="go('/onboarding/photos')">Edit</button>
              </div>

              <div *ngIf="selfPhotos.length === 0" class="block" style="opacity:.8;">No photos yet.</div>

              <div *ngIf="selfPhotos.length > 0" class="cardsGrid">
                <div class="imgCard" *ngFor="let p of selfPhotos; let i = index">
                  <img [src]="p.url" [alt]="'Photo ' + (i+1)" />
                  <div class="imgMeta" *ngIf="p.caption">Caption: {{ p.caption }}</div>
                  <div class="imgMeta" *ngIf="!p.caption" style="opacity:.55;">No caption</div>
                </div>
              </div>
            </section>

            <section class="section">
              <div class="sectionHead">
                <div>
                  <div class="hTitle">Intent</div>
                  <div class="hSub">What you’re looking for.</div>
                </div>
                <button class="linkBtn" (click)="go('/onboarding/intent')">Edit</button>
              </div>
              <div class="line">{{ selfIntentLine }}</div>
            </section>

            <!-- ✅ NEW: FOUNDATIONAL SECTION -->
            <section class="section">
              <div class="sectionHead">
                <div>
                  <div class="hTitle">Foundational</div>
                  <div class="hSub">Your deeper answers used for matching and coaching.</div>
                </div>
                <!-- Foundational answers are not editable after initial submission -->
              </div>

              <div *ngIf="foundationalQa.length === 0" class="block" style="opacity:.8;">
                No foundational answers yet.
              </div>

              <div class="qaList" *ngIf="foundationalQa.length > 0">
                <div class="qaItem" *ngFor="let item of foundationalQa">
                  <div class="qaQ">{{ item.q || ('Question ' + item.id) }}</div>
                  <div class="qaA">{{ item.a }}</div>
                </div>
              </div>
            </section>

            <section class="section">
              <div class="sectionHead">
                <div>
                  <div class="hTitle">Details</div>
                  <div class="hSub">Bio, weekly vibe, and extras.</div>
                </div>
                <button class="linkBtn" (click)="go('/onboarding/details')">Edit</button>
              </div>

              <div class="block" *ngIf="selfWeeklyVibe">
                <div class="blockLabel">Weekly vibe</div>
                <div class="blockText">{{ selfWeeklyVibe }}</div>
              </div>

              <div class="block" *ngIf="selfBio">
                <div class="blockLabel">Bio</div>
                <div class="blockText">{{ selfBio }}</div>
              </div>

              <div class="block" *ngIf="selfPreferenceFields.length">
                <div class="blockLabel">Dating preferences</div>
                <div class="kv">
                  <div class="kvRow" *ngFor="let f of selfPreferenceFields">
                    <div class="k">{{ prettyKey(f.key) }}</div>
                    <div class="v">{{ f.value }}</div>
                  </div>
                </div>
              </div>

              <div class="block" *ngIf="selfOptionalFields.length">
                <div class="blockLabel">Optional fields</div>
                <div class="kv">
                  <div class="kvRow" *ngFor="let f of selfOptionalFields">
                    <div class="k">{{ prettyKey(f.key) }}</div>
                    <div class="v">{{ f.value }}</div>
                  </div>
                </div>
              </div>

              <div class="block" *ngIf="!selfBio && !selfWeeklyVibe && !selfPreferenceFields.length && !selfOptionalFields.length" style="opacity:.8;">
                No details yet.
              </div>
            </section>

            <div class="btnRow" style="justify-content:flex-end; padding: 6px 0 2px;">
              <button class="btn ghost" (click)="reload()" [disabled]="loading || completing">Refresh</button>
              <button class="btn primary" (click)="complete()" [disabled]="loading || completing">
                {{ completing ? 'Finishing…' : 'Finish' }}
              </button>
            </div>

          </ng-container>
        </ng-container>

      </div>
    </woven-onboarding-shell>
  `,
})
export class ReviewOnboardingComponent implements OnInit {
  tab: Tab = 'self';
  loading = true;
  completing = false;
  error = '';

  data: ReviewResponse | null = null;

  @ViewChild('vstrip') vstrip?: ElementRef<HTMLDivElement>;

  captionOpenIndex: number | null = null;
  captionText = '';

  constructor(private onboarding: OnboardingService, private router: Router) {}

  async ngOnInit() {
    await this.reload();
  }

  async reload() {
    this.loading = true;
    this.error = '';
    try {
      this.data = await firstValueFrom(this.onboarding.getReview());
      this.captionOpenIndex = null;
      this.captionText = '';
    } catch (e: unknown) {
      const err = e as any;
      this.error = err?.error?.error || err?.error?.message || err?.message || 'Could not load review.';
    } finally {
      this.loading = false;
    }
  }

  go(path: string) {
    this.router.navigateByUrl(path);
  }

  toggleCaption(idx: number, caption?: string) {
    if (this.captionOpenIndex === idx) {
      this.captionOpenIndex = null;
      this.captionText = '';
      return;
    }
    this.captionOpenIndex = idx;
    const text = (caption || '').toString().trim();
    this.captionText = text.length ? text : 'No caption added for this photo.';
  }

  private get self(): any {
    return (this.data as any)?.self ?? {};
  }

  private get pub(): any {
    return (this.data as any)?.publicPreview ?? {};
  }

  // ✅ NEW: Foundational QA getter
  get foundationalQa(): FoundationalQa[] {
    const arr = this.self?.foundational?.qa;
    if (!Array.isArray(arr)) return [];
    return arr
      .map((x: any) => ({
        id: (x?.id || '').toString(),
        q: (x?.q || '').toString(),
        a: (x?.a || '').toString(),
      }))
      .filter((x: FoundationalQa) => x.id.trim().length > 0 && x.a.trim().length > 0);
  }

  // ===========================
  // PUBLIC GETTERS
  // ===========================
  get publicName(): string {
    return (this.pub?.name || this.self?.fullName || '').toString().trim() || '—';
  }

  get publicAge(): string {
    const age = this.pub?.age ?? this.self?.basics?.age;
    return age ? `${age}` : '';
  }

  get publicGender(): string {
    return (this.pub?.gender || '').toString().trim();
  }

  get publicLocation(): string {
    return (this.pub?.location || '').toString().trim();
  }

  get publicIntentPrimary(): string {
    const intent = this.pub?.intent;
    if (!intent) return '';
    return (intent.primaryIntent || '').toString().trim();
  }

  get publicIntentOpennessLine(): string {
    const intent = this.pub?.intent;
    if (!intent) return '';
    const op: any = intent.openness;
    const arr: string[] = Array.isArray(op) ? op.map((x: any) => String(x)) : [];
    return arr.length ? `Open to: ${arr.join(', ')}` : '';
  }

  get publicPhotos(): PhotoView[] {
    const p = this.pub?.photos;

    if (Array.isArray(p) && p.length > 0 && typeof p[0] === 'object') {
      return (p as any[])
        .map((x: any): PhotoView => ({
          url: (x?.url || x?.dataUrl || '').toString(),
          caption: (x?.caption || '').toString(),
          sortOrder: Number(x?.sortOrder ?? x?.order ?? 999),
        }))
        .filter((x: PhotoView) => x.url.trim().length > 0)
        .sort((a: PhotoView, b: PhotoView) => a.sortOrder - b.sortOrder);
    }

    const arr: any[] = Array.isArray(p) ? p : [];
    return arr
      .filter((x: any) => typeof x === 'string' && x.length > 0)
      .map((url: string, i: number) => ({ url, caption: '', sortOrder: i + 1 }));
  }

  get publicOptional(): PublicOptionalField[] {
    const arr: any[] = Array.isArray(this.pub?.optionalPublic) ? this.pub.optionalPublic : [];
    return arr
      .map((x: any) => ({ key: (x?.key || '').toString(), value: (x?.value || '').toString() }))
      .filter((x: PublicOptionalField) => x.key.trim().length > 0 && x.value.trim().length > 0);
  }

  get publicBio(): string {
    return (this.pub?.bio || '').toString().trim();
  }

  get publicWeeklyVibe(): string {
    return (this.self?.details?.weeklyVibe || '').toString().trim();
  }

  get publicBasicsChips(): string[] {
    const chips: string[] = [];
    if (this.publicGender) chips.push(this.publicGender);
    if (this.publicLocation) chips.push(this.publicLocation);
    if (this.publicIntentPrimary) chips.push(this.publicIntentPrimary);
    return chips;
  }

  // ===========================
  // SELF GETTERS
  // ===========================
  get selfBasicsLine(): string {
    const b = this.self?.basics || {};
    const age = b?.age ? `${b.age}` : '';
    const gender = b?.gender ? `${b.gender}` : '';
    const city = b?.location?.city || '';
    const state = b?.location?.state || '';
    const left = [age, gender].filter(Boolean).join(' • ');
    const loc = [city, state].filter(Boolean).join(', ');
    return [left, loc].filter(Boolean).join(' — ') || '—';
  }

  get selfDatingPrefsLine(): string {
    const b = this.self?.basics || {};
    const distance = b?.distanceMiles ? `Distance: ${b.distanceMiles} miles` : '';
    const interested = Array.isArray(b?.interestedIn) ? `Interested in: ${b.interestedIn.join(', ')}` : '';
    return [distance, interested].filter(Boolean).join(' • ') || '';
  }

  get selfPhotos(): PhotoView[] {
    const arr: any[] = Array.isArray(this.self?.photos) ? this.self.photos : [];
    return arr
      .map((p: any): PhotoView => ({
        url: (p?.url || p?.dataUrl || '').toString(),
        caption: (p?.caption || '').toString(),
        sortOrder: Number(p?.sortOrder ?? p?.order ?? 999),
      }))
      .filter((p: PhotoView) => typeof p.url === 'string' && p.url.length > 0)
      .sort((a: PhotoView, b: PhotoView) => a.sortOrder - b.sortOrder);
  }

  get selfIntentLine(): string {
    const intent = this.self?.intent || {};
    const primary = (intent.primaryIntent || '').toString().trim();
    const openness = Array.isArray(intent.openness) ? intent.openness.join(', ') : '';
    const reflection = (intent.reflectionSentence || '').toString().trim();
    const parts = [primary, openness ? `Open to: ${openness}` : '', reflection ? `"${reflection}"` : ''].filter(Boolean);
    return parts.join(' • ') || '—';
  }

  get selfBio(): string {
    return (this.self?.details?.bio || '').toString().trim();
  }

  get selfWeeklyVibe(): string {
    return (this.self?.details?.weeklyVibe || '').toString().trim();
  }

  get selfPreferenceFields(): any[] {
    const arr = this.self?.details?.preferenceFields;
    return Array.isArray(arr) ? arr : [];
  }

  get selfOptionalFields(): any[] {
    const arr = this.self?.details?.optionalFields;
    return Array.isArray(arr) ? arr : [];
  }

  prettyKey(k: string): string {
    if (!k) return 'Field';
    const cleaned = k.replace(/^pref_/, '').replace(/_/g, ' ');
    return cleaned.replace(/\b\w/g, (c) => c.toUpperCase());
  }

  async complete() {
    this.error = '';
    this.completing = true;
    try {
      await firstValueFrom(this.onboarding.completeOnboarding());
      await this.router.navigateByUrl('/home');
    } catch (e: unknown) {
      const err = e as any;
      this.error = err?.error?.error || err?.error?.message || err?.message || 'Could not finish onboarding.';
    } finally {
      this.completing = false;
    }
  }
}
