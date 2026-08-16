import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { firstValueFrom } from 'rxjs';

type UploadTokenResponse = {
  uploadUrl: string;
  fileUrl: string;
  mediaId: string;
};

type ConfirmResponse = {
  mediaId: string;
  fileUrl: string;
};

@Injectable({ providedIn: 'root' })
export class MediaService {
  constructor(private http: HttpClient) {}

  async uploadVoiceNote(blob: Blob, durationSecs: number): Promise<{ fileUrl: string; durationSecs: number }> {
    const ext = blob.type.includes('ogg') ? 'ogg' : blob.type.includes('mp4') ? 'mp4' : 'webm';
    const fileName = `voice-${Date.now()}.${ext}`;

    const token = await firstValueFrom(
      this.http.post<UploadTokenResponse>(`${environment.apiUrl}/media/upload-token`, {
        containerType: 'voice-note',
        fileName,
        contentType: blob.type || 'audio/webm',
      })
    );

    await fetch(token.uploadUrl, {
      method: 'PUT',
      headers: { 'x-ms-blob-type': 'BlockBlob', 'Content-Type': blob.type || 'audio/webm' },
      body: blob,
    });

    const confirmed = await firstValueFrom(
      this.http.post<ConfirmResponse>(`${environment.apiUrl}/media/confirm`, {
        mediaId: token.mediaId,
      })
    );

    return { fileUrl: confirmed.fileUrl, durationSecs };
  }
}
