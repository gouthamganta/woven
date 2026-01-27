import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-test-api',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div style="padding: 20px;">
      <h2>API Connection Test</h2>
      <button (click)="testConnection()">Test Backend Connection</button>
      
      <div *ngIf="loading" style="margin-top: 20px;">
        Loading...
      </div>
      
      <div *ngIf="result" style="margin-top: 20px; padding: 10px; background: #e8f5e9; border-radius: 5px;">
        <strong>✅ Success!</strong>
        <pre>{{ result | json }}</pre>
      </div>
      
      <div *ngIf="error" style="margin-top: 20px; padding: 10px; background: #ffebee; border-radius: 5px;">
        <strong>❌ Error:</strong>
        <pre>{{ error }}</pre>
      </div>
    </div>
  `
})
export class TestApiComponent {
  result: any = null;
  error: string = '';
  loading: boolean = false;

  constructor(private http: HttpClient) {}

  testConnection() {
    this.loading = true;
    this.result = null;
    this.error = '';

    this.http.get(`${environment.apiUrl}/health`).subscribe({
      next: (data) => {
        this.result = data;
        this.loading = false;
      },
      error: (err) => {
        this.error = err.message;
        this.loading = false;
      }
    });
  }
}