import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { IdentityCheck } from '../../shared/confirm-identity/identity-check';
import { Salutation, SalutationsStore } from './salutations.api';

const MAX_SALUTATIONS = 30;

/**
 * Master data, part 3: the user's salutations, each in English and Gujarati (Shri / શ્રી). A contact picks one; a
 * message's {{salutation}} becomes the text in the contact's language. Excel offers only these.
 */
@Component({
  selector: 'app-salutations-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './salutations.page.html',
  styleUrls: ['../../shared/approved.css', './contact-fields.page.css'],
})
export class SalutationsPage implements OnInit {
  protected readonly store = inject(SalutationsStore);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly identity = inject(IdentityCheck);

  protected readonly max = MAX_SALUTATIONS;
  /** null: closed; 'new': adding; a salutation: changing it. */
  protected readonly editing = signal<Salutation | 'new' | null>(null);
  protected readonly saving = signal(false);
  protected nameEn = '';
  protected nameGu = '';

  async ngOnInit(): Promise<void> {
    try {
      await this.store.reload();
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not load your salutations', detail: problemMessage(e), life: 6000 });
    }
  }

  protected open(s: Salutation | 'new'): void {
    this.editing.set(s);
    this.nameEn = s === 'new' ? '' : s.nameEn;
    this.nameGu = s === 'new' ? '' : s.nameGu;
  }

  /** How it will show in lists and Excel, the same rule as the server. */
  protected label(): string {
    const en = this.nameEn.trim();
    const gu = this.nameGu.trim();
    return en === gu ? en : `${en} / ${gu}`;
  }

  protected async save(): Promise<void> {
    const editing = this.editing();
    if (!editing) return;
    if (!this.nameEn.trim() || !this.nameGu.trim()) {
      this.toast.add({ severity: 'warn', summary: 'Type both: the English text (Shri) and the Gujarati text (શ્રી).' });
      return;
    }

    this.saving.set(true);
    try {
      await this.store.save(editing === 'new' ? null : editing.id, this.nameEn.trim(), this.nameGu.trim());
      await this.store.reload();
      this.editing.set(null);
      this.toast.add({ severity: 'success', summary: 'Saved' });
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not saved', detail: problemMessage(e), life: 6000 });
    } finally {
      this.saving.set(false);
    }
  }

  protected remove(s: Salutation): void {
    this.confirm.confirm({
      header: `Delete ${s.label}?`,
      message: s.contactCount > 0
        ? `${s.contactCount} ${s.contactCount === 1 ? 'contact has' : 'contacts have'} it; they are left without a salutation.`
        : 'No contact has it yet.',
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Keep',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: async () => {
        // A delete also needs the password, so nothing is deleted by a slip.
        if (!(await this.identity.ask('Confirm to delete.'))) return;
        try {
          await this.store.remove(s.id);
          await this.store.reload();
        } catch (e) {
          this.toast.add({ severity: 'error', summary: 'Not deleted', detail: problemMessage(e), life: 6000 });
        }
      },
    });
  }
}
