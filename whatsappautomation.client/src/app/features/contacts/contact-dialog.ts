import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TextareaModule } from 'primeng/textarea';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../core/http/problem';
import { moduleIcon, OccasionModule } from '../modules/modules.api';
import { MONTHS } from './contact-fields.api';
import { ContactsApi } from './contacts.api';
import { Category, Contact, ContactField, ContactOccasion, Group, LANGUAGES, LanguageCode } from './contacts.models';

/** One occasion date being edited on the form. */
interface OccasionRow {
  module: OccasionModule;
  day: number | null;
  month: number | null;
  year: number | null;
  forName: string;
}

/**
 * Add or edit one contact. Emits the saved contact; the page reloads its list.
 * Occasions: only the ones this contact has are shown; "Add occasion" lists the user's other occasions (made first
 * under Occasions). Custom fields: every field the user made, by its type.
 */
@Component({
  selector: 'app-contact-dialog',
  imports: [
    FormsModule, ReactiveFormsModule, RouterLink, ButtonModule, DialogModule, InputTextModule, MultiSelectModule, SelectModule, SelectButtonModule,
    TextareaModule,
  ],
  templateUrl: './contact-dialog.html',
  styleUrl: './contacts.css',
})
export class ContactDialog {
  private readonly api = inject(ContactsApi);

  /** null: closed. 'new': adding. A contact: editing it. */
  readonly target = input<Contact | 'new' | null>(null);
  readonly categories = input<Category[]>([]);
  readonly groups = input<Group[]>([]);
  readonly defaultLanguage = input<LanguageCode>('en');
  /** The user's occasions (empty when they may not make occasions). */
  readonly modules = input<OccasionModule[]>([]);
  /** The user's own contact fields. */
  readonly fields = input<ContactField[]>([]);
  /** The user may make occasions and fields (shows the "set up" links). */
  readonly canSetUp = input(false);
  readonly closed = output<void>();
  readonly saved = output<Contact>();

  protected readonly languages = LANGUAGES.map((l) => ({ label: l.code === 'en' ? l.label : `${l.label} · ${l.native}`, value: l.code }));
  protected readonly days = Array.from({ length: 31 }, (_, i) => ({ label: String(i + 1), value: i + 1 }));
  protected readonly months = MONTHS.map((m, i) => ({ label: m, value: i + 1 }));
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    phoneNumber: ['', [Validators.required, Validators.maxLength(30)]],
    savedName: ['', Validators.maxLength(100)],
    language: ['en' as LanguageCode, Validators.required],
    nameEn: ['', Validators.maxLength(100)],
    nameHi: ['', Validators.maxLength(100)],
    nameGu: ['', Validators.maxLength(100)],
    categoryId: [null as string | null],
    groupIds: [[] as string[]],
    salutation: ['', Validators.maxLength(50)],
    notes: ['', Validators.maxLength(1000)],
  });

  protected readonly moduleIcon = moduleIcon;
  protected readonly occasionRows = signal<OccasionRow[]>([]);
  /** Occasions this contact does not have yet, for "Add occasion". */
  protected readonly addable = computed(() =>
    this.modules().filter((m) => !this.occasionRows().some((r) => r.module.id === m.id)).map((m) => ({ label: m.name, value: m.id })),
  );
  protected readonly fieldValues = signal<Record<string, string>>({});

  protected readonly language = signal<LanguageCode>('en');
  protected readonly isNew = computed(() => this.target() === 'new');
  /** A number without WhatsApp cannot be in groups (the server drops them too). */
  protected readonly notOnWhatsApp = computed(() => {
    const t = this.target();
    return t !== null && t !== 'new' && t.whatsAppCheck === 'not_on_whatsapp';
  });
  protected readonly visible = computed(() => this.target() !== null);

  constructor() {
    this.form.controls.language.valueChanges.subscribe((l) => this.language.set(l));
    effect(() => {
      const target = this.target();
      if (target === null) return;
      this.error.set(null);
      const modules = this.modules();
      if (target === 'new') {
        this.form.reset({ language: this.defaultLanguage(), categoryId: null, groupIds: [] });
        this.occasionRows.set([]);
        this.fieldValues.set({});
      } else {
        this.occasionRows.set(
          target.occasions
            .map((o) => ({ o, module: modules.find((m) => m.id === o.moduleId) }))
            .filter((x): x is { o: ContactOccasion; module: OccasionModule } => !!x.module)
            .map(({ o, module }) => ({ module, day: o.day, month: o.month, year: o.year, forName: o.forName ?? '' })),
        );
        this.fieldValues.set({ ...target.fields });
        this.form.reset({
          phoneNumber: target.phoneNumber,
          savedName: target.savedName ?? '',
          language: target.language,
          nameEn: target.nameEn ?? '',
          nameHi: target.nameHi ?? '',
          nameGu: target.nameGu ?? '',
          categoryId: target.categoryId,
          groupIds: target.groups.map((g) => g.id),
          salutation: target.salutation ?? '',
          notes: target.notes ?? '',
        });
      }
      this.language.set(this.form.controls.language.value);
    });
  }

  protected required(code: LanguageCode): boolean {
    return this.language() === code;
  }

  protected addOccasion(moduleId: string | null): void {
    const module = this.modules().find((m) => m.id === moduleId);
    if (!module) return;
    this.occasionRows.set([...this.occasionRows(), { module, day: null, month: null, year: null, forName: '' }]);
  }

  protected patchOccasion(index: number, patch: Partial<OccasionRow>): void {
    this.occasionRows.set(this.occasionRows().map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  protected removeOccasion(index: number): void {
    this.occasionRows.set(this.occasionRows().filter((_, i) => i !== index));
  }

  protected setField(id: string, value: string | null): void {
    this.fieldValues.set({ ...this.fieldValues(), [id]: value ?? '' });
  }

  protected token(f: ContactField): string {
    return `{{${f.key}}}`;
  }

  protected choiceOptions(f: ContactField): { label: string; value: string }[] {
    return f.options.map((o) => ({ label: o, value: o }));
  }

  /** The first problem with the occasion dates, in plain words; null when they are fine. */
  private occasionProblem(): string | null {
    for (const r of this.occasionRows()) {
      if (!r.day || !r.month) return `${r.module.name}: choose the day and the month.`;
      if (r.module.kind === 'once' && !r.year) return `${r.module.name}: a one-time date needs the year.`;
      const max = new Date(r.year ?? 2024, r.month, 0).getDate();
      if (r.day > max) return `${r.module.name}: ${r.day} ${MONTHS[r.month - 1]} does not exist${r.year ? ' in ' + r.year : ''}.`;
      if (r.year && (r.year < 1900 || r.year > 2200)) return `${r.module.name}: check the year.`;
    }
    return null;
  }

  protected async save(): Promise<void> {
    const v = this.form.getRawValue();
    const name = v.language === 'hi' ? v.nameHi : v.language === 'gu' ? v.nameGu : v.nameEn;
    if (this.form.invalid || !name.trim()) {
      this.form.markAllAsTouched();
      this.error.set(!name.trim() ? `Enter the real name in ${LANGUAGES.find((l) => l.code === v.language)?.label}: messages use it.` : null);
      return;
    }

    const problem = this.occasionProblem();
    if (problem) {
      this.error.set(problem);
      return;
    }

    const target = this.target();
    this.saving.set(true);
    this.error.set(null);
    try {
      const contact = await this.api.saveContact(target && target !== 'new' ? target.id : null, {
        phoneNumber: v.phoneNumber,
        savedName: v.savedName || null,
        nameEn: v.nameEn || null,
        nameHi: v.nameHi || null,
        nameGu: v.nameGu || null,
        language: v.language,
        categoryId: v.categoryId || null,
        salutation: v.salutation || null,
        notes: v.notes || null,
        groupIds: v.groupIds,
        // Users who cannot see occasions (no modules) keep what is saved.
        occasions: this.modules().length > 0
          ? this.occasionRows().map((r) => ({
              moduleId: r.module.id,
              day: r.day!,
              month: r.month!,
              year: r.year || null,
              forName: r.module.forNameLabel ? r.forName.trim() || null : null,
            }))
          : undefined,
        fields: this.fields().length > 0
          ? Object.fromEntries(this.fields().map((f) => [f.id, (this.fieldValues()[f.id] ?? '').trim() || null]))
          : undefined,
      });
      this.saved.emit(contact);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.saving.set(false);
    }
  }
}
