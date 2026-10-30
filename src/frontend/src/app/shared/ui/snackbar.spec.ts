import { TestBed } from '@angular/core/testing';
import { text } from '../testing';
import { Snackbar, SnackbarHost } from './snackbar';

describe('Snackbar', () => {
  afterEach(() => vi.useRealTimers());

  it('shows a message with an action, runs it once and hides', async () => {
    const fixture = TestBed.createComponent(SnackbarHost);
    const snackbar = TestBed.inject(Snackbar);
    const action = vi.fn();

    snackbar.show({ text: 'Zmieniono.', actionLabel: 'Zastosuj', action });
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(text(el.querySelector('.snackbar__text'))).toBe('Zmieniono.');
    el.querySelector<HTMLButtonElement>('.snackbar__action')!.click();
    await fixture.whenStable();

    expect(action).toHaveBeenCalledTimes(1);
    expect(el.querySelector('.snackbar')).toBeNull();
  });

  it('hides itself after a while', () => {
    vi.useFakeTimers();
    const snackbar = TestBed.inject(Snackbar);

    snackbar.show({ text: 'Zmieniono.' });
    vi.advanceTimersByTime(Snackbar.durationMs);

    expect(snackbar.message()).toBeNull();
  });
});
