// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = defineConfig([
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    // The static host page is not an Angular template.
    ignores: ['src/index.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      // All UI text goes through Transloco. This flags literal text and user-facing attributes
      // (aria-label, title, placeholder, alt, ...) in templates; use {{ t('key') }} or
      // [attr.x]="t('key')" instead. We don't use Angular's i18n attribute, so the ID check is off.
      '@angular-eslint/template/i18n': [
        'error',
        {
          checkId: false,
          checkText: true,
          checkAttributes: true,
          ignoreAttributes: [
            // Technical attributes that never hold user-facing text.
            'routerLinkActive',
            'ariaCurrentWhenActive',
            'class',
            'id',
            'role',
            'type',
            'href',
            'for',
            'name',
            'autocomplete',
            'inputmode',
            'aria-labelledby',
            'aria-describedby',
            'aria-controls',
            'viewBox',
            'd',
            'accept',
            'aria-live',
          ],
        },
      ],
    },
  },
]);
