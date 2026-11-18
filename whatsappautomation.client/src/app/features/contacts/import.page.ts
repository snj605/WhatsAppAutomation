import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { problemMessage } from '../../core/http/problem';
import { ContactsApi } from './contacts.api';
import { ImportResult, ImportRow } from './contacts.models';

type Step = 'choose' | 'preview' | 'done';

/** Import contacts from Excel or CSV: download your real contacts, edit or add rows, preview every row, then import. */
@Component({
  selector: 'app-import-page',
  imports: [FormsModule, RouterLink, ButtonModule, SelectButtonModule, TableModule, TagModule],
  templateUrl: './import.page.html',
  styleUrls: ['./contacts.css', './import.page.css'],
})
export class ImportPage {
  private readonly api = inject(ContactsApi);
  private readonly toast = inject(MessageService);

  protected readonly step = signal<Step>('choose');
  protected readonly file = signal<File | null>(null);
  protected readonly result = signal<ImportResult | null>(null);
  protected readonly busy = signal(false);
  protected readonly dragging = signal(false);
  protected readonly error = signal<string | null>(null);
  protected show: 'all' | 'error' = 'all';
  protected readonly showOptions = [
    { label: 'All rows', value: 'all' },
    { label: 'Only problems', value: 'error' },
  ];
  protected readonly toSave = computed(() => (this.result()?.created ?? 0) + (this.result()?.updated ?? 0));

  protected rows(): ImportRow[] {
    const rows = this.result()?.rows ?? [];
    return this.show === 'error' ? rows.filter((r) => r.action === 'error') : rows;
  }

  protected async downloadTemplate(): Promise<void> {
    try {
      const blob = await this.api.template();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'my-contacts.xlsx';
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Download failed', detail: problemMessage(e), life: 6000 });
    }
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) void this.preview(file);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) void this.preview(file);
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected async apply(): Promise<void> {
    const file = this.file();
    if (!file) return;
    this.busy.set(true);
    try {
      this.result.set(await this.api.import(file, true));
      this.step.set('done');
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected restart(): void {
    this.file.set(null);
    this.result.set(null);
    this.error.set(null);
    this.show = 'all';
    this.step.set('choose');
  }

  /** A CSV of the rows with problems, with an Errors column, to fix and upload again. */
  protected downloadErrors(): void {
    const rows = (this.result()?.rows ?? []).filter((r) => r.action === 'error');
    const quote = (v: string | number | null) => `"${String(v ?? '').replace(/"/g, '""')}"`;
    const lines = [
      ['Row', 'Phone', 'Name', 'Category', 'Errors'].map(quote).join(','),
      ...rows.map((r) => [r.row, r.phoneNumber, r.name, r.category, r.errors.join(' ')].map(quote).join(',')),
    ];
    const blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'import-problems.csv';
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  private async preview(file: File): Promise<void> {
    this.error.set(null);
    if (!/\.(xlsx|csv)$/i.test(file.name)) {
      this.error.set('Choose an Excel (.xlsx) or CSV (.csv) file.');
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      this.error.set('The file is larger than 5 MB. Split it into smaller files.');
      return;
    }

    this.busy.set(true);
    this.file.set(file);
    try {
      this.result.set(await this.api.import(file, false));
      this.show = 'all';
      this.step.set('preview');
    } catch (e) {
      this.error.set(problemMessage(e));
      this.file.set(null);
    } finally {
      this.busy.set(false);
    }
  }
}
