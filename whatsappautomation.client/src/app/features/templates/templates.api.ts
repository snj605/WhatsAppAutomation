import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { UploadedFile } from '../files/files.api';

/** A saved message (GET /api/v1/templates): the user's own, and the shared ones of their organisation. */
export interface MessageTemplate {
  id: string;
  name: string;
  text: string;
  updatedAt: string;
  /** In the organisation's library: every member can use it. */
  shared: boolean;
  /** False for shared templates of the admin, when a member sees them. */
  canEdit: boolean;
  /** A photo, video or document that goes with the text. */
  attachment: UploadedFile | null;
}

@Injectable({ providedIn: 'root' })
export class TemplatesApi {
  private readonly http = inject(HttpClient);

  list = () => firstValueFrom(this.http.get<MessageTemplate[]>('/api/v1/templates'));
  save = (id: string | null, name: string, text: string, shared = false, attachmentId: string | null = null) =>
    firstValueFrom(
      id
        ? this.http.put<MessageTemplate>(`/api/v1/templates/${id}`, { name, text, shared, attachmentId })
        : this.http.post<MessageTemplate>('/api/v1/templates', { name, text, shared, attachmentId }),
    );
  remove = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/templates/${id}`));
}
