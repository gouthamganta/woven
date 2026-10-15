import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { MediaService } from './media.service';
import { environment } from '../../environments/environment';

describe('Voice upload failure boundaries', () => {
  let service: MediaService;
  let http: HttpTestingController;
  let upload: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), MediaService] });
    service = TestBed.inject(MediaService);
    http = TestBed.inject(HttpTestingController);
    upload = vi.fn().mockResolvedValue(new Response(null, { status: 201 }));
    vi.stubGlobal('fetch', upload);
    vi.spyOn(Date, 'now').mockReturnValue(100);
  });
  afterEach(() => { http.verify(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
  async function token() {
    http.expectOne(`${environment.apiUrl}/media/upload-token`).flush({ uploadUrl: 'https://synthetic.invalid/upload', fileUrl: '/provisional', mediaId: 'media' });
    await Promise.resolve();
    await Promise.resolve();
  }

  for (const [mime, extension] of [['audio/ogg', 'ogg'], ['audio/mp4', 'mp4'], ['audio/webm', 'webm'], ['', 'webm']]) {
    it(`uploads ${mime || 'untyped audio'} and returns the confirmed URL`, async () => {
      const blob = new Blob(['synthetic-audio'], { type: mime });
      const result = service.uploadVoiceNote(blob, 2.5);
      const request = http.expectOne(`${environment.apiUrl}/media/upload-token`);
      expect(request.request.body).toEqual({ containerType: 'voice-note', fileName: `voice-100.${extension}`, contentType: mime || 'audio/webm' });
      request.flush({ uploadUrl: 'https://synthetic.invalid/upload', fileUrl: '/provisional', mediaId: 'media' });
      await Promise.resolve(); await Promise.resolve();
      const confirm = http.expectOne(`${environment.apiUrl}/media/confirm`);
      expect(confirm.request.body).toEqual({ mediaId: 'media' });
      expect(upload).toHaveBeenCalledWith('https://synthetic.invalid/upload', {
        method: 'PUT', headers: { 'x-ms-blob-type': 'BlockBlob', 'Content-Type': mime || 'audio/webm' }, body: blob,
      });
      expect(upload.mock.calls[0][1].headers.Authorization).toBeUndefined();
      confirm.flush({ mediaId: 'media', fileUrl: '/confirmed' });
      expect(await result).toEqual({ fileUrl: '/confirmed', durationSecs: 2.5 });
    });
  }

  it('does not upload or confirm when authorization for the upload is denied', async () => {
    const result = service.uploadVoiceNote(new Blob(['synthetic']), 2);
    const assertion = expect(result).rejects.toMatchObject({ status: 403 });
    http.expectOne(`${environment.apiUrl}/media/upload-token`).flush({}, { status: 403, statusText: 'Forbidden' });
    await assertion;
    expect(upload).not.toHaveBeenCalled();
    http.expectNone(`${environment.apiUrl}/media/confirm`);
  });

  it('does not confirm after a network failure uploading the blob', async () => {
    upload.mockRejectedValue(new TypeError('Synthetic upload network failure'));
    const result = service.uploadVoiceNote(new Blob(['synthetic']), 2);
    const assertion = expect(result).rejects.toThrow('Synthetic upload network failure');
    await token();
    await assertion;
    http.expectNone(`${environment.apiUrl}/media/confirm`);
  });

  it('rejects an HTTP-denied blob upload rather than reporting a confirmed voice note', async () => {
    upload.mockResolvedValue(new Response(null, { status: 403 }));
    const result = service.uploadVoiceNote(new Blob(['synthetic']), 2);
    const assertion = expect(result).rejects.toThrow();
    await token();
    // Release an incorrect confirm request so this regression fails promptly,
    // rather than hanging on a promise. A correct implementation emits none.
    for (const request of http.match(`${environment.apiUrl}/media/confirm`)) request.flush({ mediaId: 'media', fileUrl: '/incorrect-success' });
    await assertion;
    http.expectNone(`${environment.apiUrl}/media/confirm`);
  });

  it('preserves confirmation failure rather than returning the provisional URL', async () => {
    const result = service.uploadVoiceNote(new Blob(['synthetic']), 2);
    const assertion = expect(result).rejects.toMatchObject({ status: 500 });
    await token();
    http.expectOne(`${environment.apiUrl}/media/confirm`).flush({}, { status: 500, statusText: 'Server error' });
    await assertion;
  });
});
