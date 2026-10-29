import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export const INVITATIONS_URL = '/api/invitations';

/** Mirrors `InvitationResponse` from the API. */
export interface Invitation {
  id: string;
  email: string;
  createdAt: string;
  acceptedAt: string | null;
  isOwner: boolean;
}

export const INVITE_FAILURES = ['invalidEmail', 'alreadyInvited', 'unknown'] as const;
export type InviteFailure = (typeof INVITE_FAILURES)[number];

@Injectable({ providedIn: 'root' })
export class InvitationsApi {
  private readonly http = inject(HttpClient);

  invite(email: string): Promise<Invitation> {
    return firstValueFrom(this.http.post<Invitation>(INVITATIONS_URL, { email }));
  }

  revoke(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${INVITATIONS_URL}/${id}`));
  }
}

export function inviteFailure(error: unknown): InviteFailure {
  const code =
    error instanceof HttpErrorResponse ? (error.error as { code?: unknown })?.code : null;
  return INVITE_FAILURES.find((known) => known === code) ?? 'unknown';
}
