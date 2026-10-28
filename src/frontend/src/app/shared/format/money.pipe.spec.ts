import { TestBed } from '@angular/core/testing';
import { provideLocale } from '../../core/i18n/i18n.providers';
import { MoneyPipe } from './money.pipe';

const NBSP = String.fromCharCode(0xa0);
const MINUS = String.fromCharCode(0x2212);

describe('MoneyPipe', () => {
  let pipe: MoneyPipe;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideLocale(), MoneyPipe] });
    pipe = TestBed.inject(MoneyPipe);
  });

  it('formats PLN with Polish separators', () => {
    expect(pipe.transform(1234.56)).toBe(`1${NBSP}234,56${NBSP}zł`);
  });

  it('shows expenses with a typographic minus', () => {
    expect(pipe.transform('-1234.5')).toBe(`${MINUS}1${NBSP}234,50${NBSP}zł`);
  });

  it('adds a plus to income when the sign is always shown', () => {
    expect(pipe.transform(6200, 'PLN', 'always')).toBe(`+6${NBSP}200,00${NBSP}zł`);
    expect(pipe.transform(-5, 'PLN', 'always')).toBe(`${MINUS}5,00${NBSP}zł`);
    expect(pipe.transform(0, 'PLN', 'always')).toBe(`0,00${NBSP}zł`);
  });

  it('formats foreign currencies', () => {
    expect(pipe.transform(12.3, 'EUR')).toBe(`12,30${NBSP}€`);
  });

  it('returns an empty string for missing or invalid values', () => {
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform('')).toBe('');
    expect(pipe.transform('abc')).toBe('');
  });
});
