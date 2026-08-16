import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

export interface MyTile {
  id: string;
  contentType: string;
  contentText: string | null;
  mediaUrl: string | null;
  createdAt: string;
  expiresAt: string;
  isExpired: boolean;
  isHighlighted: boolean;
  isModerated: boolean;
  highlightSlot: number | null;
}

export interface ReceivedOrbit {
  id: string;
  orbiterId: number;
  tileId: string;
  relationshipType: string;
  orbitedAt: string;
}

@Injectable({ providedIn: 'root' })
export class TilesService {
  private api = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getMine() {
    return this.http.get<{ count: number; tiles: MyTile[] }>(`${this.api}/tiles/mine`);
  }

  create(contentType: string, contentText?: string, mediaUrl?: string) {
    return this.http.post<{ tileId: string }>(`${this.api}/tiles`, {
      contentType,
      contentText: contentText ?? null,
      mediaUrl: mediaUrl ?? null,
    });
  }

  highlight(tileId: string, slotNumber: number) {
    return this.http.post<{ slot: number }>(`${this.api}/tiles/${tileId}/highlight`, { slotNumber });
  }

  unhighlight(tileId: string) {
    return this.http.delete(`${this.api}/tiles/${tileId}/highlight`);
  }

  getReceivedOrbits() {
    return this.http.get<ReceivedOrbit[]>(`${this.api}/orbit/received`);
  }
}
