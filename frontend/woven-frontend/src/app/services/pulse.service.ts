import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

export type PulseAnswers = {
  d1_battery: 'high' | 'medium' | 'low';
  d2_tone: 'playful' | 'serious' | 'calm';
  d3_role: 'driver' | 'copilot' | 'passenger';
};

export type PulseOption = {
  key: string;
  label: string;
  subLabel?: string;
};

export type PulseQuestion = {
  id: 'd1_battery' | 'd2_tone' | 'd3_role';
  text: string;
  options: PulseOption[];
};

export type PulseState = {
  cycleId: string;
  cycleStartUtc: string;
  cycleEndUtc: string;
  answered: boolean;
  answers: Partial<PulseAnswers>;
  questions: PulseQuestion[];
};

@Injectable({ providedIn: 'root' })
export class PulseService {
  private base = environment.apiUrl;
  constructor(private http: HttpClient) {}

  getCurrent() {
    return this.http.get<PulseState>(`${this.base}/intake/dynamic/current`);
  }

  submit(answers: PulseAnswers) {
    return this.http.put<{ ok: boolean; cycleId: string; answeredAtUtc: string; mappingVersion: number }>(
      `${this.base}/intake/dynamic`,
      { answers }
    );
  }
}
