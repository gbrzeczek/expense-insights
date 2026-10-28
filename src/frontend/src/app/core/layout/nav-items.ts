export interface NavItem {
  readonly path: string;
  readonly labelKey: string;
  /** SVG path data for a 24×24 icon. */
  readonly icon: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  {
    path: '/overview',
    labelKey: 'nav.overview',
    icon: 'M5 9.2h3V19H5zM10.6 5h2.8v14h-2.8zm5.6 8H19v6h-2.8z',
  },
  {
    path: '/transactions',
    labelKey: 'nav.transactions',
    icon: 'M3 13h2v-2H3zm0 4h2v-2H3zm0-8h2V7H3zm4 4h14v-2H7zm0 4h14v-2H7zM7 7v2h14V7z',
  },
  {
    path: '/categories',
    labelKey: 'nav.categories',
    icon: 'M17.63 5.84A2 2 0 0 0 16 5L5 5.01A2 2 0 0 0 3 7v10a2 2 0 0 0 2 1.99L16 19a2 2 0 0 0 1.63-.84L22 12z',
  },
  {
    path: '/import',
    labelKey: 'nav.import',
    icon: 'M9 16h6v-6h4l-7-7-7 7h4zm-4 2h14v2H5z',
  },
  {
    path: '/account',
    labelKey: 'nav.account',
    icon: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8m0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4',
  },
];
