import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** A file uploaded to send with a broadcast (POST /api/v1/files). */
export interface UploadedFile {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export const MAX_FILE_BYTES = 16 * 1024 * 1024;

/** Programs and scripts the server refuses; checked here too so the user hears it at once. */
const BLOCKED = ['.exe', '.msi', '.bat', '.cmd', '.com', '.scr', '.pif', '.ps1', '.vbs', '.vbe', '.js', '.jse', '.wsf', '.jar', '.apk', '.app', '.sh', '.dll'];

export function fileProblem(file: File): string | null {
  if (file.size === 0) return 'The file is empty.';
  if (file.size > MAX_FILE_BYTES) return 'The file is larger than 16 MB.';
  const name = file.name.toLowerCase();
  if (BLOCKED.some((ext) => name.endsWith(ext))) return 'Programs and scripts cannot be sent. Send a document, photo or video.';
  return null;
}

export function fileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function fileIcon(contentType: string): string {
  if (contentType.startsWith('image/')) return 'pi pi-image';
  if (contentType.startsWith('video/')) return 'pi pi-video';
  if (contentType === 'application/pdf') return 'pi pi-file-pdf';
  if (contentType.includes('sheet') || contentType.includes('excel')) return 'pi pi-file-excel';
  if (contentType.includes('word')) return 'pi pi-file-word';
  return 'pi pi-file';
}

@Injectable({ providedIn: 'root' })
export class FilesApi {
  private readonly http = inject(HttpClient);

  upload(file: File): Promise<UploadedFile> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<UploadedFile>('/api/v1/files', form));
  }
}
