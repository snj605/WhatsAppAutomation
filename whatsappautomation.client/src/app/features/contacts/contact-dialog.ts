import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { TextareaModule } from 'primeng/textarea';
import { problemMessage } from '../../core/http/problem';
import { ContactsApi } from './contacts.api';
import { Category, Contact, Group, LANGUAGES, LanguageCode } from './contacts.models';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** Add or edit one contact. Emits the saved contact; the page reloads its list. */
@Component({
  selector: 'app-contact-dialog',
  imports: [ReactiveFormsModule, ButtonModule, DialogModule, InputTextModule, MultiSelectModule, SelectModule, SelectButtonModule, TextareaModule],
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
    birthDay: [null as number | null],
    birthMonth: [null as number | null],
    birthYear: [null as number | null],
    notes: ['', Validators.maxLength(1000)],
  });

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
      if (target === 'new') {
        this.form.reset({ language: this.defaultLanguage(), categoryId: null, groupIds: [] });
      } else {
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
          birthDay: target.birthday?.day ?? null,
          birthMonth: target.birthday?.month ?? null,
          birthYear: target.birthday?.year ?? null,
          notes: target.notes ?? '',
        });
      }
      this.language.set(this.form.controls.language.value);
    });
  }

  protected required(code: LanguageCode): boolean {
    return this.language() === code;
  }

  protected async save(): Promise<void> {
    const v = this.form.getRawValue();
    const name = v.language === 'hi' ? v.nameHi : v.language === 'gu' ? v.nameGu : v.nameEn;
    if (this.form.invalid || !name.trim()) {
      this.form.markAllAsTouched();
      this.error.set(!name.trim() ? `Enter the real name in ${LANGUAGES.find((l) => l.code === v.language)?.label}: messages use it.` : null);
      return;
    }

    if ((v.birthDay === null) !== (v.birthMonth === null)) {
      this.error.set('For a birthday choose both the day and the month (the year is optional).');
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
        birthday: v.birthDay && v.birthMonth ? { day: v.birthDay, month: v.birthMonth, year: v.birthYear || null } : null,
        notes: v.notes || null,
        groupIds: v.groupIds,
      });
      this.saved.emit(contact);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.saving.set(false);
    }
  }
}
