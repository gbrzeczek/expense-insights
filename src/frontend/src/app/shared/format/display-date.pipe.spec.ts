import { TestBed } from '@angular/core/testing';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { DisplayDatePipe } from './display-date.pipe';

describe('DisplayDatePipe', () => {
  let pipe: DisplayDatePipe;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideLocale(), DisplayDatePipe] });
    pipe = TestBed.inject(DisplayDatePipe);
  });

  it('formats a DateOnly string as dd.MM.yyyy', () => {
    expect(pipe.transform('2026-10-28')).toBe('28.10.2026');
  });

  it('formats a Date', () => {
    expect(pipe.transform(new Date(2026, 0, 5))).toBe('05.01.2026');
  });

  it('returns an empty string for missing values', () => {
    expect(pipe.transform(null)).toBe('');
  });
});
