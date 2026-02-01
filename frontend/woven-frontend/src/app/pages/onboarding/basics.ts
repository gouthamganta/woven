import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';

import { environment } from '../../../environments/environment';
import { OnboardingService } from '../../onboarding/onboarding.service';
import { OnboardingShellComponent } from './onboarding-shell';

type Gender = 'male' | 'female' | 'nonbinary' | 'other';
type Interest = 'male' | 'female' | 'nonbinary';
type RelationshipStructure = 'OPEN' | 'MONOGAMY' | 'NON_MONOGAMY';

// ✅ STEP 1.1: Updated CityOption to include lat/lng
type CityOption = {
  label: string;
  city: string;
  state: string;
  country?: string;
  lat: number;
  lng: number;
};

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, OnboardingShellComponent],
  template: `
    <woven-onboarding-shell
      title="Basics"
      subtitle="Keep it simple. You can edit later."
      [stepNumber]="2"
      [totalSteps]="6"
      stepLabel="Profile"
    >
      <style>
        /* ===== Layout ===== */
        .stack {
          display: grid;
          gap: 20px;
        }

        .grid2 {
          display: grid;
          grid-template-columns: 1fr 1fr;
          gap: 14px;
        }

        /* ===== Labels ===== */
        .label {
          display: block;
          font-size: 12px;
          font-weight: 600;
          color: #0f0f0f;
          opacity: 0.65;
          margin-bottom: 8px;
          letter-spacing: 0.01em;
        }

        /* ===== Inputs ===== */
        .input, select {
          width: 100%;
          padding: 12px 14px;
          border: 1px solid rgba(0, 0, 0, 0.1);
          border-radius: 12px;
          outline: none;
          background: rgba(255, 255, 255, 0.9);
          font-size: 14px;
          font-weight: 500;
          color: #0f0f0f;
          transition: all 0.2s ease;
        }

        .input::placeholder {
          color: #0f0f0f;
          opacity: 0.4;
        }

        .input:focus, select:focus {
          border-color: #0f0f0f;
          box-shadow: 0 0 0 3px rgba(15, 15, 15, 0.06);
        }

        select {
          cursor: pointer;
          appearance: none;
          background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%23666' stroke-width='2'%3E%3Cpath d='M6 9l6 6 6-6'/%3E%3C/svg%3E");
          background-repeat: no-repeat;
          background-position: right 12px center;
          padding-right: 36px;
        }

        /* ===== Checkboxes ===== */
        .checks {
          display: flex;
          gap: 10px;
          flex-wrap: wrap;
        }

        .check {
          display: flex;
          align-items: center;
          gap: 8px;
          padding: 12px 16px;
          min-height: 44px;
          border-radius: 100px;
          border: 1px solid rgba(0, 0, 0, 0.1);
          background: rgba(255, 255, 255, 0.8);
          cursor: pointer;
          transition: all 0.2s ease;
          font-size: 13px;
          font-weight: 550;
        }

        .check:hover {
          border-color: rgba(0, 0, 0, 0.2);
          background: rgba(255, 255, 255, 1);
        }

        .check input[type="checkbox"] {
          width: 16px;
          height: 16px;
          accent-color: #0f0f0f;
          cursor: pointer;
        }

        /* ===== Range Indicators ===== */
        .rangeRow {
          display: flex;
          justify-content: space-between;
          font-size: 11px;
          font-weight: 600;
          color: #0f0f0f;
          opacity: 0.45;
          margin-top: 8px;
        }

        /* ===== Segment Buttons ===== */
        .segment {
          display: flex;
          gap: 8px;
          flex-wrap: wrap;
        }

        .segbtn {
          padding: 12px 18px;
          min-height: 44px;
          border-radius: 100px;
          border: 1px solid rgba(0, 0, 0, 0.1);
          background: rgba(255, 255, 255, 0.8);
          cursor: pointer;
          font-weight: 600;
          font-size: 13px;
          color: #0f0f0f;
          transition: all 0.2s ease;
        }

        .segbtn:hover:not(.active) {
          border-color: rgba(0, 0, 0, 0.2);
          background: rgba(255, 255, 255, 1);
        }

        .segbtn.active {
          background: linear-gradient(135deg, #0f0f0f 0%, #1a1a1a 100%);
          color: #fff;
          border-color: transparent;
          box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
        }

        .segbtn:focus-visible {
          outline: 2px solid #0f0f0f;
          outline-offset: 2px;
        }

        /* ===== Range Slider ===== */
        input[type="range"] {
          width: 100%;
          height: 6px;
          border-radius: 100px;
          background: rgba(0, 0, 0, 0.08);
          outline: none;
          -webkit-appearance: none;
          appearance: none;
        }

        input[type="range"]::-webkit-slider-thumb {
          -webkit-appearance: none;
          appearance: none;
          width: 20px;
          height: 20px;
          border-radius: 50%;
          background: #0f0f0f;
          cursor: pointer;
          border: 3px solid #fff;
          box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
          transition: transform 0.15s ease;
        }

        input[type="range"]::-webkit-slider-thumb:hover {
          transform: scale(1.1);
        }

        input[type="range"]::-moz-range-thumb {
          width: 20px;
          height: 20px;
          border-radius: 50%;
          background: #0f0f0f;
          cursor: pointer;
          border: 3px solid #fff;
          box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
        }

        /* ===== Primary Button ===== */
        .btn {
          width: 100%;
          padding: 14px 18px;
          border-radius: 14px;
          border: 0;
          background: linear-gradient(135deg, #0f0f0f 0%, #1a1a1a 100%);
          color: #fff;
          cursor: pointer;
          font-weight: 650;
          font-size: 15px;
          letter-spacing: -0.01em;
          transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
          box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
          margin-top: 8px;
        }

        .btn:hover:not(:disabled) {
          transform: translateY(-1px);
          box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
        }

        .btn:active:not(:disabled) {
          transform: translateY(0);
        }

        .btn:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }

        .btn:focus-visible {
          outline: 2px solid #0f0f0f;
          outline-offset: 2px;
        }

        /* ===== Error ===== */
        .error {
          padding: 12px 14px;
          border-radius: 12px;
          background: rgba(176, 0, 32, 0.06);
          border: 1px solid rgba(176, 0, 32, 0.12);
          color: #b00020;
          font-size: 13px;
          font-weight: 500;
          line-height: 1.4;
        }

        /* ===== Responsive ===== */
        @media (max-width: 480px) {
          .grid2 {
            grid-template-columns: 1fr;
            gap: 16px;
          }

          .segment {
            gap: 8px;
          }

          .segbtn {
            padding: 12px 16px;
            min-height: 44px;
            font-size: 13px;
          }
        }
      </style>

      <div class="stack">
        <div>
          <label class="label">Full name</label>
          <input
            class="input"
            [(ngModel)]="model.fullName"
            placeholder="Your name"
            autocomplete="name"
          />
        </div>

        <div class="grid2">
          <div>
            <label class="label">Age</label>
            <input
              class="input"
              type="number"
              [(ngModel)]="model.age"
              min="18"
              max="100"
              inputmode="numeric"
            />
          </div>

          <div>
            <label class="label">Gender</label>
            <select [(ngModel)]="model.gender">
              <option value="" disabled>Select</option>
              <option *ngFor="let g of genderOptions" [value]="g.value">{{ g.label }}</option>
            </select>
          </div>
        </div>

        <!-- ✅ Location dropdown -->
        <div>
          <label class="label">Location</label>
          <select
            [(ngModel)]="selectedCity"
            (ngModelChange)="applyCitySelection($event)"
          >
            <option [ngValue]="null" disabled>Select a city</option>
            <option *ngFor="let c of cityOptions" [ngValue]="c">
              {{ c.label }}
            </option>
          </select>

          <div class="rangeRow" *ngIf="model.location.city && model.location.state">
            <span>Selected:</span>
            <span><b>{{ model.location.city }}</b>, <b>{{ model.location.state }}</b></span>
          </div>
        </div>

        <div>
          <label class="label">Interested in</label>
          <div class="checks">
            <label *ngFor="let opt of interestOptions" class="check">
              <input
                type="checkbox"
                [checked]="model.interestedIn.includes(opt.value)"
                (change)="toggleInterest(opt.value)"
              />
              <span>{{ opt.label }}</span>
            </label>
          </div>
        </div>

        <!-- ✅ Relationship structure -->
        <div>
          <label class="label">Relationship structure</label>
          <div class="segment">
            <button
              type="button"
              class="segbtn"
              *ngFor="let opt of relationshipOptions"
              [class.active]="model.relationshipStructure === opt.value"
              (click)="model.relationshipStructure = opt.value"
            >
              {{ opt.label }}
            </button>
          </div>
        </div>

        <div>
          <label class="label">
            Distance: <b>{{ model.distanceMiles }}</b> miles
          </label>

          <!-- backend enforces 15..100 -->
          <input
            type="range"
            min="15"
            max="100"
            [(ngModel)]="model.distanceMiles"
          />

          <div class="rangeRow">
            <span>15</span><span>100</span>
          </div>
        </div>

        <div>
          <label class="label">
            Preferred age range: <b>{{ model.ageMin }}</b> – <b>{{ model.ageMax }}</b>
          </label>

          <div class="grid2">
            <div>
              <label class="label">Min</label>
              <input
                class="input"
                type="number"
                min="18"
                max="99"
                [(ngModel)]="model.ageMin"
                (blur)="normalizeAgeRange()"
              />
            </div>

            <div>
              <label class="label">Max</label>
              <input
                class="input"
                type="number"
                min="18"
                max="99"
                [(ngModel)]="model.ageMax"
                (blur)="normalizeAgeRange()"
              />
            </div>
          </div>

          <div class="rangeRow">
            <span>18</span><span>99</span>
          </div>
        </div>

        <div *ngIf="error" class="error">{{ error }}</div>

        <button class="btn" type="button" (click)="submit()" [disabled]="saving">
          {{ saving ? 'Saving…' : 'Continue' }}
        </button>
      </div>
    </woven-onboarding-shell>
  `
})
export class BasicsOnboardingComponent {
  saving = false;
  error = '';

  genderOptions = [
    { label: 'Male', value: 'male' as Gender },
    { label: 'Female', value: 'female' as Gender },
    { label: 'Non-binary', value: 'nonbinary' as Gender },
    { label: 'Other', value: 'other' as Gender }
  ];

  interestOptions = [
    { label: 'Women', value: 'female' as Interest },
    { label: 'Men', value: 'male' as Interest },
    { label: 'Non-binary', value: 'nonbinary' as Interest }
  ];

  relationshipOptions = [
    { label: 'Open', value: 'OPEN' as RelationshipStructure },
    { label: 'Monogamy', value: 'MONOGAMY' as RelationshipStructure },
    { label: 'Non-monogamy', value: 'NON_MONOGAMY' as RelationshipStructure }
  ];

  // ✅ STEP 0 & 1: Beta cities only with coordinates
  cityOptions: CityOption[] = [
    { label: 'Austin, TX (USA)', city: 'Austin', state: 'TX', country: 'USA', lat: 30.2672, lng: -97.7431 },
    // Optional 2nd city (uncomment if needed):
    // { label: 'New York, NY (USA)', city: 'New York', state: 'NY', country: 'USA', lat: 40.7128, lng: -74.0060 },
  ];

  selectedCity: CityOption | null = null;

  model = {
    fullName: '',
    age: 25,
    gender: '' as Gender | '',
    interestedIn: [] as Interest[],
    distanceMiles: 25,
    ageMin: 18,
    ageMax: 99,
    relationshipStructure: 'OPEN' as RelationshipStructure,
    location: { city: '', state: '', lat: 0, lng: 0 }
  };

  constructor(
    private http: HttpClient,
    private onboarding: OnboardingService,
    private router: Router
  ) {
    // SSR-safe localStorage check
    const isBrowser = typeof window !== 'undefined' && !!window.localStorage;
    if (isBrowser) {
      const raw = localStorage.getItem('user');
      if (raw) {
        try {
          const u = JSON.parse(raw);
          if (u?.fullName) this.model.fullName = u.fullName;
        } catch {}
      }
    }

    // ✅ STEP 2: Set default selection and apply coords
    this.selectedCity = this.cityOptions[0];
    this.applyCitySelection(this.selectedCity);
  }

  // ✅ STEP 2: Updated to write lat/lng into model
  applyCitySelection(c: CityOption | null) {
    if (!c) return;
    this.model.location.city = c.city;
    this.model.location.state = c.state;
    this.model.location.lat = c.lat;
    this.model.location.lng = c.lng;
  }

  toggleInterest(v: Interest) {
    const set = new Set(this.model.interestedIn);
    set.has(v) ? set.delete(v) : set.add(v);
    this.model.interestedIn = Array.from(set);
  }

  normalizeAgeRange() {
    const clamp = (n: number, min: number, max: number) => Math.min(max, Math.max(min, n));

    this.model.ageMin = clamp(Number(this.model.ageMin || 18), 18, 99);
    this.model.ageMax = clamp(Number(this.model.ageMax || 99), 18, 99);

    // ensure min <= max
    if (this.model.ageMin > this.model.ageMax) {
      const tmp = this.model.ageMin;
      this.model.ageMin = this.model.ageMax;
      this.model.ageMax = tmp;
    }
  }

  private validate(): string | null {
    if (!this.model.fullName?.trim()) return 'Full name is required.';
    if (!this.model.age || this.model.age < 18) return 'Age must be 18 or above.';
    if (!this.model.gender) return 'Please select your gender.';
    if (!this.model.location.city?.trim() || !this.model.location.state?.trim()) return 'City and state are required.';
    if (!this.model.interestedIn?.length) return 'Please select who you\'re interested in.';

    // backend validations
    if (this.model.distanceMiles < 15 || this.model.distanceMiles > 100) {
      return 'Distance must be between 15 and 100 miles.';
    }

    if (this.model.ageMin < 18 || this.model.ageMax > 99) return 'Preferred age range must be between 18 and 99.';
    if (this.model.ageMin > this.model.ageMax) return 'Preferred age min cannot be greater than max.';
    return null;
  }

  async submit() {
    // Normalize age range before validation
    this.normalizeAgeRange();

    const err = this.validate();
    if (err) {
      this.error = err;
      return;
    }

    this.error = '';
    this.saving = true;

    try {
      await firstValueFrom(
        this.http.put(`${environment.apiUrl}/onboarding/basics`, {
          fullName: this.model.fullName.trim(),
          age: this.model.age,
          gender: this.model.gender,
          interestedIn: this.model.interestedIn,
          relationshipStructure: this.model.relationshipStructure,
          distanceMiles: this.model.distanceMiles,
          ageMin: this.model.ageMin,
          ageMax: this.model.ageMax,
          // ✅ STEP 3: Stop hardcoding 0,0 - use actual coords from model
          location: {
            city: this.model.location.city.trim(),
            state: this.model.location.state.trim(),
            lat: this.model.location.lat,
            lng: this.model.location.lng
          }
        })
      );

      // ✅ Force next step to Photos (frontend-only fix)
      this.router.navigateByUrl('/onboarding/photos');
    } catch {
      this.error = 'Could not save basics. Please try again.';
    } finally {
      this.saving = false;
    }
  }
}