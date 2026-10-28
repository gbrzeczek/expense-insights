import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Session } from '../../core/auth/session';

type Pending = 'logout' | 'delete' | null;

/** Who is signed in, sign out, the owner's way to invitations, and deleting the account. */
@Component({
  selector: 'app-account-page',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account-page.html',
  styleUrl: './account-page.scss',
})
export class AccountPage {
  private readonly router = inject(Router);
  protected readonly session = inject(Session);

  protected readonly confirming = signal(false);
  protected readonly pending = signal<Pending>(null);
  protected readonly failed = signal<Pending>(null);

  protected async logout(): Promise<void> {
    await this.run('logout', () => this.session.logout());
  }

  protected async deleteAccount(): Promise<void> {
    await this.run('delete', () => this.session.deleteAccount());
  }

  private async run(action: Exclude<Pending, null>, work: () => Promise<void>): Promise<void> {
    this.pending.set(action);
    this.failed.set(null);
    try {
      await work();
      await this.router.navigateByUrl('/login');
    } catch {
      this.failed.set(action);
    } finally {
      this.pending.set(null);
    }
  }
}
