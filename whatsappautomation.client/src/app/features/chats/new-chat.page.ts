import { Component, computed, effect, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { ComposerPreset } from '../birthdays/birthday-wishes';
import { BroadcastsApi } from '../broadcasts/broadcasts.api';
import { BroadcastInput, BroadcastPreview } from '../broadcasts/broadcasts.models';
import { ContactsApi } from '../contacts/contacts.api';
import { Category, Contact, displayName, Group } from '../contacts/contacts.models';
import { initialsOf } from './chat-format';
import { fileIcon, fileProblem, FilesApi, fileSize, UploadedFile } from '../files/files.api';
import { MessageTemplate, TemplatesApi } from '../templates/templates.api';
import { MORE_INSERTS, NAME_PLACEHOLDER, SEND_TEMPLATES } from './send-templates';
import { WhatsAppGate } from '../whatsapp/whatsapp-gate';

export type Audience = 'all' | 'categories' | 'groups' | 'people';
type Step = 1 | 2 | 3;

interface Picked {
  id: string;
  label: string;
}

const PAGE = 200;

/**
 * Send a message in three clear steps:
 *  1. Who: everyone, one or more categories, one or more groups, or people picked by hand.
 *  2. Message: a ready template or your own words, "Add name" for each person's name, and a live phone preview.
 *  3. Check and send: how many get it, who is skipped and why, how long it takes; then Send.
 * Everyone gets their own message with their details filled in, sent one by one at a safe pace.
 */
@Component({
  selector: 'app-new-chat-page',
  imports: [RouterLink],
  templateUrl: './new-chat.page.html',
  styleUrl: './new-chat.page.css',
})
export class NewChatPage implements OnInit {
  private readonly contactsApi = inject(ContactsApi);
  private readonly api = inject(BroadcastsApi);
  private readonly router = inject(Router);
  private readonly toast = inject(MessageService);
  private readonly templatesApi = inject(TemplatesApi);
  private readonly filesApi = inject(FilesApi);
  private readonly gate = inject(WhatsAppGate);

  /** The file sent with the message (the message is its caption). */
  protected readonly attachment = signal<UploadedFile | null>(null);
  protected readonly uploading = signal<string | null>(null);
  protected readonly fileSize = fileSize;
  protected readonly fileIcon = fileIcon;
  private readonly box = viewChild<ElementRef<HTMLTextAreaElement>>('box');

  /** The user's own saved messages, shown first in step 2. */
  protected readonly myTemplates = signal<MessageTemplate[]>([]);
  protected readonly saveOpen = signal(false);
  protected readonly templateName = signal('');

  protected readonly templates = SEND_TEMPLATES;
  protected readonly moreInserts = MORE_INSERTS;
  protected readonly initials = initialsOf;
  protected readonly displayName = displayName;

  protected readonly step = signal<Step>(1);

  // Step 1: who
  protected readonly audience = signal<Audience | null>(null);
  protected readonly categories = signal<Category[]>([]);
  protected readonly groups = signal<Group[]>([]);
  protected readonly allCount = signal(0);
  protected readonly categoryIds = signal<ReadonlySet<string>>(new Set());
  protected readonly groupIds = signal<ReadonlySet<string>>(new Set());
  protected readonly picked = signal<ReadonlyMap<string, Picked>>(new Map());
  protected readonly query = signal('');
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly contactsTotal = signal(0);
  protected readonly loadingContacts = signal(false);

  // Step 2: message
  protected readonly text = signal('');
  protected readonly showMore = signal(false);
  protected readonly sampleIndex = signal(0);
  private readonly presetName = signal<string | null>(null);
  private readonly forBirthday = signal(false);

  // Preview and sending
  protected readonly preview = signal<BroadcastPreview | null>(null);
  protected readonly previewing = signal(false);
  protected readonly showSkipped = signal(false);
  protected readonly sending = signal(false);

  /** Rough count before the server answers (people in several lists count once there). */
  protected readonly roughCount = computed(() => {
    switch (this.audience()) {
      case 'all':
        return this.allCount();
      case 'categories':
        return this.categories().filter((c) => this.categoryIds().has(c.id)).reduce((n, c) => n + c.contactCount, 0);
      case 'groups':
        return this.groups().filter((g) => this.groupIds().has(g.id)).reduce((n, g) => n + g.contactCount, 0);
      case 'people':
        return this.picked().size;
      default:
        return 0;
    }
  });

  protected readonly whoLabel = computed(() => {
    switch (this.audience()) {
      case 'all':
        return 'All contacts';
      case 'categories':
        return this.categories().filter((c) => this.categoryIds().has(c.id)).map((c) => c.name).join(', ');
      case 'groups':
        return this.groups().filter((g) => this.groupIds().has(g.id)).map((g) => g.name).join(', ');
      case 'people': {
        const names = [...this.picked().values()].map((p) => p.label);
        return names.length <= 3 ? names.join(', ') : `${names.slice(0, 3).join(', ')} and ${names.length - 3} more`;
      }
      default:
        return '';
    }
  });

  protected readonly canLeaveStep1 = computed(() => this.roughCount() > 0);
  protected readonly canLeaveStep2 = computed(() => this.text().trim().length > 0);
  protected readonly willSend = computed(() => this.preview()?.willSend ?? 0);
  protected readonly skippedCount = computed(() => {
    const p = this.preview();
    return p ? p.total - p.willSend : 0;
  });
  protected readonly sample = computed(() => {
    const samples = this.preview()?.samples ?? [];
    return samples.length === 0 ? null : samples[this.sampleIndex() % samples.length];
  });
  protected readonly sampleCount = computed(() => this.preview()?.samples.length ?? 0);

  /** What the phone preview says when there is no example to show. */
  protected readonly previewNote = computed(() => {
    const p = this.preview();
    if (!this.text().trim()) return 'Your message will appear here as each person sees it.';
    if (this.previewing() || !p) return 'Preparing preview…';
    if (p.total === 0) return 'Nobody to send to. Go back to step 1 and choose who should get it.';
    return 'Nobody can get this message as written. See why below.';
  });
  /** Reasons people are skipped, grouped: "No Hindi name: 3". */
  protected readonly skipReasons = computed(() => {
    const counts = new Map<string, number>();
    for (const s of this.preview()?.skipped ?? []) counts.set(s.reason, (counts.get(s.reason) ?? 0) + 1);
    return [...counts.entries()].map(([reason, count]) => ({ reason, count }));
  });

  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private previewTimer: ReturnType<typeof setTimeout> | undefined;
  private loadSeq = 0;
  private previewSeq = 0;

  constructor() {
    // Keep the preview fresh while writing (step 2) and on the check step.
    effect(() => {
      const input = this.input();
      const step = this.step();
      clearTimeout(this.previewTimer);
      if (step === 1 || !input.text?.trim()) {
        this.preview.set(null);
        return;
      }

      this.previewTimer = setTimeout(() => void this.loadPreview(input), step === 3 ? 0 : 600);
    });
  }

  async ngOnInit(): Promise<void> {
    // Opened from Birthdays or the Chats banner: people and a text are ready, so start at the message step.
    const preset = (history.state as { preset?: ComposerPreset } | null)?.preset;
    if (preset) {
      this.presetName.set(preset.name);
      this.forBirthday.set(preset.forBirthday === true);
      this.text.set(preset.text);
      this.picked.set(new Map(preset.people.map((p) => [p.id, p])));
      this.audience.set('people');
      this.step.set(2);
    }

    this.templatesApi
      .list()
      .then((list) => this.myTemplates.set(list))
      .catch(() => undefined); // the built-in suggestions still work

    try {
      const [categories, groups, all] = await Promise.all([
        this.contactsApi.categories(),
        this.contactsApi.groups(),
        this.contactsApi.contacts({ page: 1, pageSize: 1 }),
      ]);
      this.categories.set(categories);
      this.groups.set(groups);
      this.allCount.set(all.total);
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not load your contacts', detail: problemMessage(e), life: 6000 });
    }
  }

  // ---------- Step 1 ----------

  protected choose(audience: Audience): void {
    this.audience.set(audience);
    if (audience === 'people' && this.contacts().length === 0) void this.loadContacts();
  }

  protected toggleCategory(id: string): void {
    this.categoryIds.set(toggled(this.categoryIds(), id));
  }

  protected toggleGroup(id: string): void {
    this.groupIds.set(toggled(this.groupIds(), id));
  }

  protected onSearch(value: string): void {
    this.query.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.loadContacts(), 300);
  }

  protected isPicked(id: string): boolean {
    return this.picked().has(id);
  }

  protected togglePerson(c: Contact): void {
    const next = new Map(this.picked());
    if (next.has(c.id)) next.delete(c.id);
    else next.set(c.id, { id: c.id, label: displayName(c) });
    this.picked.set(next);
  }

  /** Ticks everyone matching the search, including those not on screen yet. */
  protected async pickAllFound(): Promise<void> {
    try {
      const all = [...this.contacts()];
      for (let page = 2; all.length < this.contactsTotal(); page++) {
        const more = await this.contactsApi.contacts({ search: this.query().trim(), page, pageSize: PAGE });
        if (more.items.length === 0) break;
        all.push(...more.items);
      }

      const next = new Map(this.picked());
      for (const c of all) next.set(c.id, { id: c.id, label: displayName(c) });
      this.picked.set(next);
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not select everyone', detail: problemMessage(e), life: 6000 });
    }
  }

  protected clearPicked(): void {
    this.picked.set(new Map());
  }

  // ---------- Step 2 ----------

  protected useTemplate(text: string): void {
    this.text.set(text);
    this.focusBoxAtEnd();
  }

  protected setText(value: string): void {
    this.text.set(value);
  }

  protected addName(): void {
    this.insert(NAME_PLACEHOLDER);
  }

  /** Puts a placeholder where the cursor is, with a space before it when needed. */
  protected insert(value: string): void {
    const el = this.box()?.nativeElement;
    const current = this.text();
    const start = el?.selectionStart ?? current.length;
    const end = el?.selectionEnd ?? current.length;
    const piece = (start > 0 && !/\s$/.test(current.slice(0, start)) ? ' ' : '') + value;
    this.text.set(current.slice(0, start) + piece + current.slice(end));
    setTimeout(() => {
      el?.focus();
      el?.setSelectionRange(start + piece.length, start + piece.length);
    });
  }

  /** Uploads the chosen file at once, so Send does not wait for it. */
  protected async attach(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = ''; // choosing the same file again still triggers a change
    if (!file) return;
    const problem = fileProblem(file);
    if (problem) {
      this.toast.add({ severity: 'warn', summary: 'Cannot attach this file', detail: problem, life: 6000 });
      return;
    }

    this.uploading.set(file.name);
    try {
      this.attachment.set(await this.filesApi.upload(file));
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not attached', detail: problemMessage(e), life: 6000 });
    } finally {
      this.uploading.set(null);
    }
  }

  protected removeAttachment(): void {
    this.attachment.set(null);
  }

  /** Saves the current message as one of the user's templates. */
  protected async saveTemplate(): Promise<void> {
    const name = this.templateName().trim();
    if (!name || !this.text().trim()) return;
    try {
      const saved = await this.templatesApi.save(null, name, this.text().trim());
      this.myTemplates.update((list) => [...list, saved].sort((a, b) => a.name.localeCompare(b.name)));
      this.saveOpen.set(false);
      this.templateName.set('');
      this.toast.add({ severity: 'success', summary: 'Saved as template', detail: saved.name });
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not saved', detail: problemMessage(e), life: 6000 });
    }
  }

  protected nextSample(): void {
    this.sampleIndex.update((i) => i + 1);
  }

  /** The text as the user sees it while writing: placeholders shown as readable words. */
  protected friendly(text: string): string {
    return text
      .replace(/\{\{\s*name_gu\s*\}\}/gi, '[Gujarati name]')
      .replace(/\{\{\s*name_hi\s*\}\}/gi, '[Hindi name]')
      .replace(/\{\{\s*name_en\s*\}\}/gi, '[English name]')
      .replace(/\{\{\s*name\s*\}\}/gi, '[Name]')
      .replace(/\{\{\s*salutation\s*\}\}/gi, '[Salutation]');
  }

  // ---------- Navigation ----------

  protected goTo(step: Step): void {
    if (step >= 2 && !this.canLeaveStep1()) return;
    if (step === 3 && !this.canLeaveStep2()) return;
    this.step.set(step);
    if (step === 2) this.focusBoxAtEnd();
  }

  protected back(): void {
    const s = this.step();
    if (s === 1) {
      void this.router.navigate(['/chats']);
    } else {
      this.step.set((s - 1) as Step);
    }
  }

  protected async send(): Promise<void> {
    if (this.sending() || this.willSend() === 0) return;
    this.sending.set(true);
    if (!(await this.gate.ensureConnected())) {
      this.sending.set(false);
      return;
    }

    try {
      const created = await this.api.create(this.input());
      this.toast.add({ severity: 'success', summary: 'Sending started', detail: `${created.counts.pending} people will get it one by one.` });
      await this.router.navigate(['/chats', created.id], { replaceUrl: true });
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not sent', detail: problemMessage(e), life: 6000 });
    } finally {
      this.sending.set(false);
    }
  }

  // ---------- Helpers ----------

  private input(): BroadcastInput {
    const audience = this.audience();
    const text = this.text().trim();
    return {
      name: this.presetName() ?? chatName(text),
      text,
      textEn: null,
      textHi: null,
      textGu: null,
      allContacts: audience === 'all',
      attachmentId: this.attachment()?.id ?? null,
      forBirthday: audience === 'people' && this.forBirthday(),
      categoryIds: audience === 'categories' ? [...this.categoryIds()] : [],
      groupIds: audience === 'groups' ? [...this.groupIds()] : [],
      contactIds: audience === 'people' ? [...this.picked().keys()] : [],
    };
  }

  private focusBoxAtEnd(): void {
    setTimeout(() => {
      const el = this.box()?.nativeElement;
      if (!el) return;
      el.focus();
      el.setSelectionRange(el.value.length, el.value.length);
    });
  }

  private async loadContacts(): Promise<void> {
    const seq = ++this.loadSeq;
    this.loadingContacts.set(true);
    try {
      const page = await this.contactsApi.contacts({ search: this.query().trim(), page: 1, pageSize: PAGE });
      if (seq !== this.loadSeq) return;
      this.contacts.set(page.items);
      this.contactsTotal.set(page.total);
    } catch (e) {
      if (seq === this.loadSeq) this.toast.add({ severity: 'error', summary: 'Could not load contacts', detail: problemMessage(e), life: 6000 });
    } finally {
      if (seq === this.loadSeq) this.loadingContacts.set(false);
    }
  }

  private async loadPreview(input: BroadcastInput): Promise<void> {
    const seq = ++this.previewSeq;
    this.previewing.set(true);
    try {
      const preview = await this.api.preview(input);
      if (seq === this.previewSeq) this.preview.set(preview);
    } catch {
      if (seq === this.previewSeq) this.preview.set(null);
    } finally {
      if (seq === this.previewSeq) this.previewing.set(false);
    }
  }
}

function toggled(set: ReadonlySet<string>, id: string): ReadonlySet<string> {
  const next = new Set(set);
  if (next.has(id)) next.delete(id);
  else next.add(id);
  return next;
}

/** A chat's name from its first words, without placeholders, e.g. "Happy Diwali" (only the user sees it). */
export function chatName(text: string): string {
  const plain = text
    .replace(/\{\{[^}]*\}\}|\{[^}]*\}/g, ' ')
    .replace(/\s+/g, ' ')
    .replace(/^[\s,.!:;-]+/, '')
    .trim();
  const short = plain.length > 40 ? plain.slice(0, 40).trimEnd() + '…' : plain;
  return short || 'Message ' + new Date().toLocaleDateString('en-IN', { day: 'numeric', month: 'short' });
}
