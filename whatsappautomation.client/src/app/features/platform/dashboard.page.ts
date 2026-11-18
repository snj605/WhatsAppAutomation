import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SkeletonModule } from 'primeng/skeleton';
import { problemMessage } from '../../core/http/problem';
import { whenText } from '../modules/time-text';
import { AuditEntry, Dashboard, DashboardApi } from './dashboard.api';

/**
 * The platform admin's start screen: how many organisations, people and numbers there are, messages today, numbers
 * that need a look, each organisation's numbers, and the audit log. Counts only: no contacts or message text.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, SkeletonModule],
  templateUrl: './dashboard.page.html',
  styleUrls: ['../../shared/approved.css', './dashboard.page.css'],
})
export class DashboardPage implements OnInit {
  private readonly api = inject(DashboardApi);
  protected readonly whenText = whenText;

  protected readonly data = signal<Dashboard | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly audit = signal<AuditEntry[] | null>(null);
  protected readonly auditTotal = signal(0);
  protected readonly auditPage = signal(1);

  protected readonly failRate = computed(() => {
    const d = this.data();
    return d && d.messagesToday > 0 ? Math.round((d.failedToday / d.messagesToday) * 100) : 0;
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.data.set(await this.api.dashboard());
      this.error.set(null);
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  protected async showAudit(page = 1): Promise<void> {
    try {
      const result = await this.api.audit(page);
      this.audit.set(result.items);
      this.auditTotal.set(result.total);
      this.auditPage.set(page);
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  protected pages(): number {
    return Math.max(1, Math.ceil(this.auditTotal() / 50));
  }

  protected statusText(s: string): string {
    return s === 'number_in_use' ? 'Number used by another account' : s.replace(/_/g, ' ');
  }
}
