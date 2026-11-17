import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  Category, Contact, ContactFilter, ContactInput, ContactPage, Group, ImportResult, LanguageCode, UpcomingBirthday, WhatsAppCheckSummary,
  BirthdaySettings,
} from './contacts.models';

/** Calls to the signed-in user's own contacts, categories, groups and import. */
@Injectable({ providedIn: 'root' })
export class ContactsApi {
  private readonly http = inject(HttpClient);

  categories = () => firstValueFrom(this.http.get<Category[]>('/api/v1/categories'));
  saveCategory = (id: string | null, name: string, color: string) =>
    firstValueFrom(
      id
        ? this.http.put<Category>(`/api/v1/categories/${id}`, { name, color })
        : this.http.post<Category>('/api/v1/categories', { name, color }),
    );
  deleteCategory = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/categories/${id}`));

  groups = () => firstValueFrom(this.http.get<Group[]>('/api/v1/groups'));
  saveGroup = (id: string | null, name: string) =>
    firstValueFrom(id ? this.http.put<Group>(`/api/v1/groups/${id}`, { name }) : this.http.post<Group>('/api/v1/groups', { name }));
  deleteGroup = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/groups/${id}`));
  changeCategoryMembers = (categoryId: string, contactIds: string[], add: boolean) =>
    firstValueFrom(this.http.post<Category>(`/api/v1/categories/${categoryId}/members`, { contactIds, add }));
  changeMembers = (groupId: string, contactIds: string[], add: boolean) =>
    firstValueFrom(this.http.post<Group>(`/api/v1/groups/${groupId}/members`, { contactIds, add }));

  contacts(filter: ContactFilter) {
    let params = new HttpParams().set('page', filter.page).set('pageSize', filter.pageSize);
    if (filter.search?.trim()) params = params.set('search', filter.search.trim());
    if (filter.categoryId) params = params.set('categoryId', filter.categoryId);
    if (filter.groupId) params = params.set('groupId', filter.groupId);
    if (filter.needsDetails) params = params.set('needsDetails', true);
    if (filter.whatsApp) params = params.set('whatsApp', filter.whatsApp);
    return firstValueFrom(this.http.get<ContactPage>('/api/v1/contacts', { params }));
  }

  saveContact = (id: string | null, input: ContactInput) =>
    firstValueFrom(id ? this.http.put<Contact>(`/api/v1/contacts/${id}`, input) : this.http.post<Contact>('/api/v1/contacts', input));
  birthdaySettings = () => firstValueFrom(this.http.get<BirthdaySettings>('/api/v1/birthdays/settings'));
  saveBirthdaySettings = (enabled: boolean, time: string | null, templateId: string | null) =>
    firstValueFrom(this.http.put<BirthdaySettings>('/api/v1/birthdays/settings', { enabled, time, templateId }));
  birthdays = (days = 7) => firstValueFrom(this.http.get<UpcomingBirthday[]>('/api/v1/birthdays', { params: { days } }));
  setOptOut = (id: string, optedOut: boolean) =>
    firstValueFrom(this.http.put<Contact>(`/api/v1/contacts/${id}/opt-out`, { optedOut }));
  whatsAppCheck = () => firstValueFrom(this.http.get<WhatsAppCheckSummary>('/api/v1/contacts/whatsapp-check'));
  /** Queues numbers for the WhatsApp check: every never-checked contact, or these contacts again. */
  requestWhatsAppCheck = (contactIds: string[] | null = null) =>
    firstValueFrom(this.http.post<WhatsAppCheckSummary>('/api/v1/contacts/whatsapp-check', { contactIds }));
  deleteContacts = (ids: string[]) => firstValueFrom(this.http.post<{ deleted: number }>('/api/v1/contacts/delete', { ids }));

  template = () => firstValueFrom(this.http.get('/api/v1/contacts/import/template', { responseType: 'blob' }));
  import(file: File, apply: boolean) {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<ImportResult>('/api/v1/contacts/import', form, { params: { apply } }));
  }

  preferences = () => firstValueFrom(this.http.get<{ defaultLanguage: LanguageCode }>('/api/v1/preferences'));
  setDefaultLanguage = (defaultLanguage: LanguageCode) =>
    firstValueFrom(this.http.put<{ defaultLanguage: LanguageCode }>('/api/v1/preferences', { defaultLanguage }));
}
