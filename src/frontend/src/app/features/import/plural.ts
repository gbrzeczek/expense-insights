const rules = new Map<string, Intl.PluralRules>();

/**
 * The CLDR plural category for a count (`one`, `few`, `many`, `other` in Polish), used to pick
 * an i18n key such as `import.result.duplicates.few`.
 */
export function pluralCategory(count: number, locale: string): Intl.LDMLPluralRule {
  let rule = rules.get(locale);
  if (!rule) {
    rule = new Intl.PluralRules(locale);
    rules.set(locale, rule);
  }
  return rule.select(count);
}
