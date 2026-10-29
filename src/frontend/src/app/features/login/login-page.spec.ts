import { TestBed } from '@angular/core/testing';
import { text, translocoTesting } from '../../shared/testing';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [LoginPage, translocoTesting] });
  });

  async function render(inputs: { error?: string; returnUrl?: string } = {}) {
    const fixture = TestBed.createComponent(LoginPage);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('offers Google sign-in, coming back to where the user was going', async () => {
    const el = await render({ returnUrl: '/import' });

    const link = el.querySelector<HTMLAnchorElement>('a.login__google')!;
    expect(text(link)).toBe('Zaloguj się przez Google');
    expect(link.getAttribute('href')).toBe('/api/auth/login/google?returnUrl=%2Fimport');
    expect(el.querySelector('[role=alert]')).toBeNull();
  });

  it('never returns to another site', async () => {
    const el = await render({ returnUrl: '//evil.example' });

    expect(el.querySelector('a.login__google')?.getAttribute('href')).toBe(
      '/api/auth/login/google?returnUrl=%2F',
    );
  });

  it('explains "no access" and offers another account', async () => {
    const el = await render({ error: 'noAccess' });

    expect(text(el.querySelector('[role=alert]'))).toContain('Brak dostępu');
    expect(text(el.querySelector('[role=alert]'))).toContain('nie ma zaproszenia');
    expect(text(el.querySelector('a.login__google'))).toBe('Zaloguj się innym kontem Google');
  });

  it('words unknown errors as a failed sign-in', async () => {
    const el = await render({ error: 'something' });

    expect(text(el.querySelector('[role=alert]'))).toBe(
      'Nie udało się zalogować. Spróbuj ponownie.',
    );
  });

  it('has the dev login in dev builds, through the same allow-list', async () => {
    const el = await render({ returnUrl: '/account' });

    const form = el.querySelector<HTMLFormElement>('form.login__dev')!;
    expect(form.getAttribute('action')).toBe('/api/auth/dev-login');
    expect(form.method).toBe('get');
    expect(form.querySelector<HTMLInputElement>('input[name=returnUrl]')?.value).toBe('/account');
  });
});
