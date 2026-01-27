import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

export interface OnboardingStateResponse {
  profileStatus: string;
  nextRoute: string;
  completed: string[];
  // Optional fields when FOUNDATIONAL_DUE
  version?: number;
  hardBlock?: boolean;
  allowSkip?: boolean;
}

export type FoundationalQuestion = { id: string; text: string };

export interface FoundationalQuestionsResponse {
  version: number;
  questions: FoundationalQuestion[];
}

/**
 * ✅ Photos payload types (must match backend schema)
 * Backend expects:
 * {
 *   "photos": [
 *     { "url": "...", "caption": "...", "sortOrder": 1 }
 *   ]
 * }
 */
export type PhotoPayloadItem = {
  url: string;          // base64 data URL for MVP (later can be CDN/S3 URL)
  caption?: string;     // <= 40
  sortOrder: number;    // 1..6
};

export type SavePhotosPayload = {
  photos: PhotoPayloadItem[];
};

/**
 * Review response can be flexible.
 * For your planned UI:
 * - Tab 1: self (private preview)
 * - Tab 2: publicPreview (what others see)
 *
 * Keep loose fallback fields too in case backend returns a flatter shape.
 */
export interface ReviewResponse {
  profileStatus?: string;
  self?: any;
  publicPreview?: any;

  basics?: any;
  intent?: any;
  photos?: any[];
  bio?: string;
  weeklyVibe?: string;
  foundational?: any;

  [key: string]: any;
}

@Injectable({ providedIn: 'root' })
export class OnboardingService {
  constructor(private http: HttpClient) {}

  getState() {
    return this.http.get<OnboardingStateResponse>(`${environment.apiUrl}/onboarding/state`);
  }

  // Foundational
  getFoundationalQuestions() {
    return this.http.get<FoundationalQuestionsResponse>(
      `${environment.apiUrl}/onboarding/foundational/questions`
    );
  }

  submitFoundationalAnswers(payload: { answers: { questionId: string; answer: string }[] }) {
    return this.http.put<{ profileStatus: string; nextRoute: string }>(
      `${environment.apiUrl}/onboarding/foundational`,
      payload
    );
  }

  deferFoundational() {
    return this.http.post<{ message: string; nextRoute: string }>(
      `${environment.apiUrl}/onboarding/foundational/defer`,
      {}
    );
  }

  // ✅ Photos
  savePhotos(payload: SavePhotosPayload) {
    return this.http.put<{ message: string; count: number }>(
      `${environment.apiUrl}/onboarding/photos`,
      payload
    );
  }

  // ✅ Details
  saveDetails(payload: { bio: string; optionalFields: any[]; weeklyVibe?: string }) {
    return this.http.put<{ profileStatus?: string; nextRoute?: string }>(
      `${environment.apiUrl}/onboarding/details`,
      payload
    );
  }

  // ✅ Review
  getReview() {
    return this.http.get<ReviewResponse>(`${environment.apiUrl}/onboarding/review`);
  }

  // ✅ Complete
  completeOnboarding() {
    return this.http.post<{ profileStatus?: string; nextRoute?: string }>(
      `${environment.apiUrl}/onboarding/complete`,
      {}
    );
  }
}
