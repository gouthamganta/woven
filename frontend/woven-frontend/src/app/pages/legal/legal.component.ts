import { Component, OnInit, Inject, PLATFORM_ID, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

@Component({
  selector: 'app-legal',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="legal-page">
      <header class="legal-header">
        <a class="back" routerLink="/login">← Woven</a>
        <nav class="tabs">
          <a [class.active]="page === 'privacy'"   routerLink="/privacy">Privacy Policy</a>
          <a [class.active]="page === 'terms'"     routerLink="/terms">Terms of Service</a>
          <a [class.active]="page === 'data'"      routerLink="/data-policy">Data Policy</a>
        </nav>
      </header>

      <main class="legal-body">

        <!-- ── Privacy Policy ─────────────────────────── -->
        <ng-container *ngIf="page === 'privacy'">
          <h1>Privacy Policy</h1>
          <p class="meta">Effective date: January 1, 2025 · Last updated: May 2025</p>

          <h2>1. What We Collect</h2>
          <p>When you use Woven, we collect information you give us directly — your name, photos, age, gender, preferences, and anything you share in your profile or conversations. We also collect technical data automatically, including your device type, IP address, and usage patterns, to operate and improve the service.</p>

          <h2>2. How We Use It</h2>
          <p>We use your information to match you with other users, power the app's features, and send service-related communications. We do not sell your personal data to third parties. We may share anonymised, aggregated data for research purposes.</p>

          <h2>3. Photos and Media</h2>
          <p>Photos you upload are stored securely and used solely to display your profile to potential matches. We do not use your photos to train AI models without your explicit consent.</p>

          <h2>4. Location</h2>
          <p>Woven does not require or collect your precise GPS location. Any location preferences you set (e.g. city-level discovery radius) are stored as profile settings only.</p>

          <h2>5. Data Retention</h2>
          <p>We retain your data for as long as your account is active. You may request deletion of your account and all associated data at any time from Settings → Delete Account.</p>

          <h2>6. Security</h2>
          <p>We use industry-standard encryption in transit (TLS) and at rest. Access to user data is restricted to authorised personnel only.</p>

          <h2>7. Your Rights</h2>
          <p>Depending on your jurisdiction, you may have the right to access, correct, port, or delete your personal data. Contact us at <a href="mailto:privacy@woven.app">privacy&#64;woven.app</a> to exercise any of these rights.</p>

          <h2>8. Changes</h2>
          <p>We will notify you of material changes to this policy via email or an in-app notice at least 14 days before they take effect.</p>
        </ng-container>

        <!-- ── Terms of Service ────────────────────────── -->
        <ng-container *ngIf="page === 'terms'">
          <h1>Terms of Service</h1>
          <p class="meta">Effective date: January 1, 2025 · Last updated: May 2025</p>

          <h2>1. Eligibility</h2>
          <p>You must be at least 18 years old to use Woven. By creating an account you confirm that you meet this requirement and that all information you provide is accurate.</p>

          <h2>2. Acceptable Use</h2>
          <p>You agree not to harass, threaten, or impersonate other users. You may not post illegal content, spam, or content that infringes third-party rights. Woven reserves the right to suspend or terminate accounts that violate these rules.</p>

          <h2>3. Your Content</h2>
          <p>You own the content you post. By posting it on Woven you grant us a limited, non-exclusive licence to display it within the platform for the purpose of operating the service.</p>

          <h2>4. Matching and Communication</h2>
          <p>Woven facilitates introductions but makes no guarantees about match quality, compatibility, or outcomes. Any in-person meetings arranged through the platform are at your own risk.</p>

          <h2>5. Subscriptions and Payments</h2>
          <p>Some features may require a paid subscription. Subscriptions auto-renew unless cancelled before the renewal date. Refunds are handled in accordance with applicable app store policies.</p>

          <h2>6. Disclaimer of Warranties</h2>
          <p>Woven is provided "as is" without warranties of any kind. We do not guarantee uninterrupted or error-free service.</p>

          <h2>7. Limitation of Liability</h2>
          <p>To the fullest extent permitted by law, Woven's liability for any claim arising from your use of the service is limited to the amount you paid us in the 12 months preceding the claim.</p>

          <h2>8. Governing Law</h2>
          <p>These terms are governed by the laws of the jurisdiction in which Woven is registered, without regard to conflict of law principles.</p>
        </ng-container>

        <!-- ── Data Policy ─────────────────────────────── -->
        <ng-container *ngIf="page === 'data'">
          <h1>Data Policy</h1>
          <p class="meta">Effective date: January 1, 2025 · Last updated: May 2025</p>

          <h2>1. Data We Store</h2>
          <p>Profile information (name, age, photos, bio, preferences), conversation history, match history, and behavioural signals such as swipe patterns used to improve match quality.</p>

          <h2>2. Data We Do Not Store</h2>
          <p>Precise GPS coordinates, payment card numbers (handled by our payment processor), or any biometric data beyond photos you voluntarily upload.</p>

          <h2>3. Third-Party Services</h2>
          <p>We use Google Sign-In for authentication, which is subject to Google's own privacy policy. We use cloud infrastructure providers bound by data processing agreements. We do not use advertising SDKs.</p>

          <h2>4. Data Transfers</h2>
          <p>Your data may be processed in countries outside your own. We use standard contractual clauses to ensure adequate protection for international transfers.</p>

          <h2>5. Cookies and Tracking</h2>
          <p>Woven uses only essential session cookies required for authentication. We do not use advertising cookies or cross-site tracking.</p>

          <h2>6. Data Breach Notification</h2>
          <p>In the event of a data breach affecting your personal data, we will notify you and the relevant authorities within the timeframes required by applicable law.</p>

          <h2>7. Contact</h2>
          <p>For any data-related enquiries, email <a href="mailto:data@woven.app">data&#64;woven.app</a>.</p>
        </ng-container>

      </main>
    </div>
  `,
  styles: [`
    .legal-page {
      min-height: 100vh;
      background: transparent;
      color: var(--text-primary);
      font-family: var(--font-ui);
    }

    .legal-header {
      position: sticky;
      top: 0;
      z-index: 10;
      display: flex;
      align-items: center;
      gap: 32px;
      padding: 16px 32px;
      background: rgba(14, 9, 18, 0.88);
      backdrop-filter: blur(20px);
      -webkit-backdrop-filter: blur(20px);
      border-bottom: 1px solid rgba(212, 160, 23, 0.10);
      flex-wrap: wrap;
    }

    .back {
      font-family: var(--font-display);
      font-size: 18px;
      font-weight: 300;
      color: var(--text-primary);
      text-decoration: none;
      letter-spacing: 0.04em;
      white-space: nowrap;
    }
    .back:hover { color: var(--gold-300); }

    nav.tabs {
      display: flex;
      gap: 4px;
      flex-wrap: wrap;
    }

    nav.tabs a {
      padding: 8px 16px;
      border-radius: 9999px;
      font-size: 13px;
      font-weight: 600;
      letter-spacing: 0.04em;
      color: var(--text-muted);
      text-decoration: none;
      transition: color 0.2s, background 0.2s;
    }

    nav.tabs a:hover { color: var(--text-secondary); }

    nav.tabs a.active {
      background: rgba(212, 160, 23, 0.12);
      color: var(--gold-300);
      border: 1px solid rgba(212, 160, 23, 0.18);
    }

    .legal-body {
      max-width: 720px;
      margin: 0 auto;
      padding: 48px 32px 96px;
    }

    h1 {
      font-family: var(--font-display);
      font-size: var(--text-4xl);
      font-weight: 300;
      letter-spacing: -0.02em;
      margin: 0 0 8px;
      background: linear-gradient(135deg, var(--gold-300), var(--text-primary));
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
    }

    .meta {
      font-size: var(--text-xs);
      color: var(--text-dim);
      letter-spacing: 0.08em;
      text-transform: uppercase;
      margin: 0 0 48px;
    }

    h2 {
      font-family: var(--font-ui);
      font-size: var(--text-base);
      font-weight: 700;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      color: var(--text-muted);
      margin: 36px 0 10px;
    }

    p {
      font-size: var(--text-base);
      line-height: 1.8;
      color: var(--text-secondary);
      margin: 0 0 8px;
    }

    a { color: var(--gold-300); text-decoration: none; }
    a:hover { text-decoration: underline; }

    @media (max-width: 600px) {
      .legal-header { padding: 12px 16px; gap: 12px; }
      .legal-body   { padding: 32px 16px 80px; }
      h1 { font-size: var(--text-3xl); }
    }
  `],
})
export class LegalComponent implements OnInit {
  page: 'privacy' | 'terms' | 'data' = 'privacy';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    @Inject(PLATFORM_ID) private pid: object,
  ) {}

  ngOnInit() {
    const path = this.router.url.split('/')[1];
    if (path === 'terms')       this.page = 'terms';
    else if (path === 'data-policy') this.page = 'data';
    else                        this.page = 'privacy';
  }
}
