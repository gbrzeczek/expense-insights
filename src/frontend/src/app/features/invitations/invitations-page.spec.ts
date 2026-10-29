import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { text, translocoTesting } from '../../shared/testing';
import { Invitation, INVITATIONS_URL } from './invitations.api';
import { InvitationsPage } from './invitations-page';

const owner: Invitation = {
  id: 'i-owner',
  email: 'owner@example.test',
  createdAt: '2026-10-28T10:00:00Z',
  acceptedAt: '2026-10-28T10:05:00Z',
  isOwner: true,
};
const accepted: Invitation = {
  id: 'i-anna',
  email: 'anna@example.test',
  createdAt: '2026-10-20T10:00:00Z',
  acceptedAt: '2026-10-21T18:30:00Z',
  isOwner: false,
};
const pending: Invitation = {
  id: 'i-piotr',
  email: 'piotr@example.test',
  createdAt: '2026-10-27T10:00:00Z',
  acceptedAt: null,
  isOwner: false,
};

describe('InvitationsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [InvitationsPage, translocoTesting],
      providers: [
        provideLocale(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(list: Invitation[]) {
    const fixture = TestBed.createComponent(InvitationsPage);
    TestBed.tick();
    http.expectOne(INVITATIONS_URL).flush(list);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  /** Lets awaited requests resolve and renders; not whenStable(), which waits for the reload. */
  async function settle() {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  }

  async function submit(el: HTMLElement, email: string) {
    const input = el.querySelector<HTMLInputElement>('#invite-email')!;
    input.value = email;
    input.dispatchEvent(new Event('input'));
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  it('lists who is invited and who has signed in', async () => {
    const { el } = await render([owner, accepted, pending]);

    const items = Array.from(el.querySelectorAll('.list__item'), (li) => text(li));
    expect(items).toEqual([
      'owner@example.test Właściciel',
      'anna@example.test Zalogowano 21.10.2026',
      'piotr@example.test Czeka na pierwsze logowanie Cofnij',
    ]);
    expect(el.querySelector('.list__revoke')?.getAttribute('aria-label')).toBe(
      'Cofnij zaproszenie dla piotr@example.test',
    );
  });

  it('invites an address and refreshes the list', async () => {
    const { el } = await render([owner]);

    await submit(el, ' piotr@example.test ');
    const post = http.expectOne({ method: 'POST', url: INVITATIONS_URL });
    expect(post.request.body).toEqual({ email: 'piotr@example.test' });
    post.flush(pending);
    await settle();
    http.expectOne(INVITATIONS_URL).flush([owner, pending]);
    await settle();

    expect(text(el.querySelector('.invite__status'))).toBe('Zaproszono piotr@example.test.');
    expect(el.querySelector<HTMLInputElement>('#invite-email')!.value).toBe('');
    expect(el.querySelectorAll('.list__item')).toHaveLength(2);
  });

  it('words the server’s error codes', async () => {
    const { el } = await render([owner]);

    await submit(el, 'anna@example.test');
    http
      .expectOne({ method: 'POST', url: INVITATIONS_URL })
      .flush({ code: 'alreadyInvited' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(text(el.querySelector('.invite__failure'))).toBe('Ten adres ma już zaproszenie.');
  });

  it('revokes a pending invitation', async () => {
    const { el } = await render([owner, pending]);

    el.querySelector<HTMLButtonElement>('.list__revoke')!.click();
    http
      .expectOne({ method: 'DELETE', url: `${INVITATIONS_URL}/i-piotr` })
      .flush(null, { status: 204, statusText: '' });
    await settle();
    http.expectOne(INVITATIONS_URL).flush([owner]);
    await settle();

    expect(el.querySelectorAll('.list__item')).toHaveLength(1);
  });
});
