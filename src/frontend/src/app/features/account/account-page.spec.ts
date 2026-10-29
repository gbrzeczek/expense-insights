import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { LOGOUT_URL, ME_URL } from '../../core/auth/auth.models';
import { Session } from '../../core/auth/session';
import { me } from '../../core/auth/testing';
import { text, translocoTesting } from '../../shared/testing';
import { AccountPage } from './account-page';

describe('AccountPage', () => {
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccountPage, translocoTesting],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  async function render(user = me()) {
    TestBed.inject(Session).user.set(user);
    const fixture = TestBed.createComponent(AccountPage);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  function button(el: HTMLElement, label: string): HTMLButtonElement {
    const found = Array.from(el.querySelectorAll('button')).find((b) => text(b) === label);
    if (!found) {
      throw new Error(`No button "${label}"`);
    }
    return found;
  }

  it('shows who is signed in', async () => {
    const { el } = await render();

    expect(text(el.querySelector('.profile'))).toContain('Zalogowano jako');
    expect(text(el.querySelector('.profile__name'))).toBe('Anna Nowak');
    expect(text(el.querySelector('.profile__email'))).toBe('anna@example.test');
    expect(el.querySelector('a[href="/invitations"]')).toBeNull();
  });

  it('signs out and goes to the login page', async () => {
    const { fixture, el } = await render();

    button(el, 'Wyloguj się').click();
    http
      .expectOne({ method: 'POST', url: LOGOUT_URL })
      .flush(null, { status: 204, statusText: '' });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/login');
  });

  it('deletes the account only after a confirmation', async () => {
    const { fixture, el } = await render();

    button(el, 'Usuń konto').click();
    await fixture.whenStable();
    expect(text(el.querySelector('.danger__confirm'))).toContain(
      'Usunąć konto i wszystkie dane na zawsze?',
    );
    http.expectNone(ME_URL);

    button(el, 'Tak, usuń wszystko').click();
    http.expectOne({ method: 'DELETE', url: ME_URL }).flush(null, { status: 204, statusText: '' });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/login');
    expect(TestBed.inject(Session).user()).toBeNull();
  });

  it('says so when deleting fails', async () => {
    const { fixture, el } = await render();

    button(el, 'Usuń konto').click();
    await fixture.whenStable();
    button(el, 'Tak, usuń wszystko').click();
    http.expectOne({ method: 'DELETE', url: ME_URL }).flush(null, { status: 500, statusText: '' });
    await fixture.whenStable();

    expect(text(el.querySelector('[role=alert]'))).toBe(
      'Nie udało się usunąć konta. Spróbuj ponownie.',
    );
    expect(navigate).not.toHaveBeenCalled();
  });

  it('gives the owner invitations and no delete button', async () => {
    const { el } = await render(me({ role: 'owner' }));

    expect(text(el.querySelector('a[href="/invitations"]'))).toContain('Zaproszenia');
    expect(text(el.querySelector('.danger'))).toContain('Konta właściciela nie można usunąć');
    expect(() => button(el, 'Usuń konto')).toThrow();
  });
});
