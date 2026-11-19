import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { IdentityCheck } from '../../shared/confirm-identity/identity-check';
import { ContactFieldsStore, FIELD_TYPES } from './contact-fields.api';
import { ContactField, ContactFieldType } from './contacts.models';

const MAX_FIELDS = 30;

/**
 * Master data, part 2: the user's own contact fields (City, Village, Relation, …). Next to Occasions. Each field is
 * filled on the contact form, used in messages as {{key}}, gets its own Excel column and can be searched.
 */
@Component({
  selector: 'app-contact-fields-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './contact-fields.page.html',
  styleUrls: ['../../shared/approved.css', './contact-fields.page.css'],
})
export class ContactFieldsPage implements OnInit {
  protected readonly store = inject(ContactFieldsStore);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly identity = inject(IdentityCheck);

  protected readonly types = FIELD_TYPES;
  protected readonly max = MAX_FIELDS;
  /** null: closed; 'new': adding; a field: changing it. */
  protected readonly editing = signal<ContactField | 'new' | null>(null);
  protected readonly saving = signal(false);
  protected name = '';
  protected type: ContactFieldType = 'text';
  protected options = '';

  async ngOnInit(): Promise<void> {
    try {
      await this.store.reload();
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not load your fields', detail: problemMessage(e), life: 6000 });
    }
  }

  protected open(f: ContactField | 'new'): void {
    this.editing.set(f);
    this.name = f === 'new' ? '' : f.name;
    this.type = f === 'new' ? 'text' : f.type;
    this.options = f === 'new' ? '' : f.options.join('\n');
  }

  protected typeLabel(t: ContactFieldType): string {
    return FIELD_TYPES.find((x) => x.value === t)?.label ?? t;
  }

  /** The placeholder this name becomes, the same rule as the server. */
  protected keyOf(name: string): string {
    return name
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '')
      .slice(0, 40);
  }

  protected token(key: string): string {
    return `{{${key}}}`;
  }

  private optionList(): string[] {
    return this.options
      .split(/\n|,/)
      .map((o) => o.trim())
      .filter((o) => o.length > 0);
  }

  protected async save(): Promise<void> {
    const editing = this.editing();
    if (!editing) return;
    if (!this.name.trim()) {
      this.toast.add({ severity: 'warn', summary: 'Give the field a name.' });
      return;
    }

    if (!this.keyOf(this.name)) {
      this.toast.add({ severity: 'warn', summary: 'Use at least one English letter or digit in the name: it becomes {{…}} in messages.' });
      return;
    }

    if (this.type === 'choice' && this.optionList().length < 2) {
      this.toast.add({ severity: 'warn', summary: 'A choice field needs at least two choices, one per line.' });
      return;
    }

    this.saving.set(true);
    try {
      if (editing === 'new') {
        await this.store.create(this.name.trim(), this.type, this.optionList());
      } else {
        await this.store.update(editing.id, this.name.trim(), this.optionList());
      }

      await this.store.reload();
      this.editing.set(null);
      this.toast.add({ severity: 'success', summary: 'Saved' });
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not saved', detail: problemMessage(e), life: 6000 });
    } finally {
      this.saving.set(false);
    }
  }

  protected remove(f: ContactField): void {
    this.confirm.confirm({
      header: `Delete ${f.name}?`,
      message: f.contactCount > 0
        ? `${f.contactCount} ${f.contactCount === 1 ? 'contact has' : 'contacts have'} a value in it; those values are deleted too. Messages already sent stay.`
        : 'No contact has a value in it yet.',
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Keep',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: async () => {
        // A delete also needs the password, so nothing is deleted by a slip.
        if (!(await this.identity.ask('Confirm to delete.'))) return;
        try {
          await this.store.remove(f.id);
          await this.store.reload();
        } catch (e) {
          this.toast.add({ severity: 'error', summary: 'Not deleted', detail: problemMessage(e), life: 6000 });
        }
      },
    });
  }
}
