import { pluralCategory } from './plural';

describe('pluralCategory', () => {
  it('follows Polish plural rules', () => {
    expect([1, 2, 4, 5, 12, 22, 25, 120, 0].map((n) => pluralCategory(n, 'pl-PL'))).toEqual([
      'one',
      'few',
      'few',
      'many',
      'many',
      'few',
      'many',
      'many',
      'many',
    ]);
  });

  it('follows English plural rules', () => {
    expect([1, 2].map((n) => pluralCategory(n, 'en'))).toEqual(['one', 'other']);
  });
});
