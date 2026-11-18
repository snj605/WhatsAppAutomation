import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ContactField, ContactFieldType } from './contacts.models';

/** The user's own contact fields (/api/v1/contact-fields), shared by the contact form, the list and the setup page. */
@Injectable({ providedIn: 'root' })
export class ContactFieldsStore {
  private readonly http = inject(HttpClient);
  readonly fields = signal<ContactField[]>([]);
  readonly loaded = signal(false);

  async reload(): Promise<void> {
    try {
      this.fields.set(await firstValueFrom(this.http.get<ContactField[]>('/api/v1/contact-fields')));
    } finally {
      this.loaded.set(true);
    }
  }

  create = (name: string, type: ContactFieldType, options: string[]) =>
    firstValueFrom(this.http.post<ContactField>('/api/v1/contact-fields', { name, type, options }));
  update = (id: string, name: string, options: string[]) =>
    firstValueFrom(this.http.put<ContactField>(`/api/v1/contact-fields/${id}`, { name, options }));
  remove = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/contact-fields/${id}`));
}

/** How a stored value reads: dates as "5 Mar 2019", the rest as stored. The same as in messages. */
export function fieldValueText(field: ContactField, stored: string | undefined | null): string {
  if (!stored) return '';
  if (field.type === 'date') {
    const [y, m, d] = stored.split('-').map(Number);
    if (y && m && d) return `${d} ${MONTHS[m - 1]} ${y}`;
  }
  return stored;
}

export const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

export const FIELD_TYPES: { value: ContactFieldType; label: string; hint: string }[] = [
  { value: 'text', label: 'Text', hint: 'Any words, for example a city or village' },
  { value: 'number', label: 'Number', hint: 'For example family members' },
  { value: 'date', label: 'Date', hint: 'A full date, shown as 5 Mar 2019' },
  { value: 'choice', label: 'Choice', hint: 'One of a list, for example Uncle / Aunt / Friend' },
];
