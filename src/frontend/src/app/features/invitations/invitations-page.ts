import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  Invitation,
  INVITATIONS_URL,
  InvitationsApi,
  InviteFailure,
  inviteFailure,
} from './invitations.api';

/** The owner's allow-list: invite a Google address, see who signed in, revoke pending ones. */
@Component({
  selector: 'app-invitations-page',
  imports: [TranslocoDirective, RouterLink, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invitations-page.html',
  styleUrl: './invitations-page.scss',
})
export class InvitationsPage {
  private readonly api = inject(InvitationsApi);

  protected readonly invitations = httpResource<Invitation[]>(() => INVITATIONS_URL, {
    defaultValue: [],
  });
  protected readonly email = signal('');
  protected readonly saving = signal(false);
  protected readonly failure = signal<InviteFailure | null>(null);
  protected readonly added = signal<string | null>(null);
  protected readonly revokeFailed = signal(false);

  protected async invite(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving()) {
      return;
    }
    this.saving.set(true);
    this.failure.set(null);
    this.added.set(null);
    try {
      const invitation = await this.api.invite(this.email().trim());
      this.added.set(invitation.email);
      this.email.set('');
      this.invitations.reload();
    } catch (error) {
      this.failure.set(inviteFailure(error));
    } finally {
      this.saving.set(false);
    }
  }

  protected async revoke(invitation: Invitation): Promise<void> {
    this.revokeFailed.set(false);
    try {
      await this.api.revoke(invitation.id);
      this.invitations.reload();
    } catch {
      this.revokeFailed.set(true);
    }
  }
}
