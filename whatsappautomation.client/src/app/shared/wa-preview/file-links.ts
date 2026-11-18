import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * Short-lived links to file content (GET /api/v1/files/{id}/view-link), so <img> and <video> can show a photo or
 * video without the sign-in token. One link per file is reused until a minute before it ends.
 */
@Injectable({ providedIn: 'root' })
export class FileLinks {
  private readonly http = inject(HttpClient);
  private readonly cache = new Map<string, { url: string; until: number }>();

  async urlFor(fileId: string): Promise<string | null> {
    const cached = this.cache.get(fileId);
    if (cached && cached.until > Date.now()) return cached.url;
    try {
      const link = await firstValueFrom(this.http.get<{ url: string; expiresAt: string }>(`/api/v1/files/${fileId}/view-link`));
      this.cache.set(fileId, { url: link.url, until: new Date(link.expiresAt).getTime() - 60_000 });
      return link.url;
    } catch {
      // Freed after sending, or not the user's: the preview shows the file card instead.
      return null;
    }
  }
}
