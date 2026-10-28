# Warm Paper Finance — Design System v1.2

> Source of truth for all UI work in this repository (see `AGENTS.md`). Sections up to
> *CSS starter tokens* are the specification; *Implementation in this repository* at the
> end records how it maps to code and the decisions taken while implementing it.
>
> **Changelog**
> - v1.2 (2026-10-28): editorial serif changed from Instrument Serif to **Source Serif 4**.
>   Instrument Serif is a condensed display face and looked squashed at headline sizes. None of
>   the references uses a condensed serif: reMarkable, Kinfolk and The Gentlewoman (Lyon Text)
>   use normal-width book serifs at ~36–52 px with near-normal tracking. Display size capped at
>   52 px; display tracking −0.005em, headings 0.
> - v1.1: original specification.

## Purpose and direction

Build a calm, trustworthy personal-finance app focused on expense analysis, spending patterns, and useful insights. Its visual language combines the paper-like restraint of reMarkable, the editorial typography of Kinfolk, and a softened, less yellow version of Gruvbox Light.

**Creative shorthand:** A beautifully typeset financial journal with the precision of a modern analytics tool.

The interface should feel warm, thoughtful, legible, and understated. It should not look like a crypto dashboard, a luxury boutique, a retro terminal, or a generic SaaS admin panel.

## Visual references and how to interpret them

These are **directional references, not layouts to copy**. The original products and websites may change over time. When implementing, review them visually, focusing on composition, typography, spacing, and color rather than literal components. The proposed palette and fonts in this specification remain the source of truth.

### Primary references

**1. reMarkable — the overall attitude and product restraint**  
https://remarkable.com/  
Useful product reference: https://remarkable.com/shop

- Borrow: paper-like neutral surfaces, charcoal-on-light contrast, minimal interface chrome, precision and generous breathing room around a focal object or idea.
- Translate to finance: a calm account overview, one dominant amount, concise period comparison, understated dividers.
- Do not borrow: extreme sparsity that forces financial tables or navigation to become inefficient; actual brand assets or device UI.

**2. Kinfolk — editorial typography and spatial hierarchy**  
https://www.kinfolk.com/  
Useful archive: https://www.kinfolk.com/product-category/kinfolk-magazines/

- Borrow: confident serif display type, strong scale contrast, consistent grid structure, generous outer margins, carefully paced sections.
- Translate to finance: a beautifully typeset overview header and occasional insight headline, followed by tight, accurate sans-serif data modules.
- Do not borrow: magazine-like huge type in transaction tables, editorial layout at the expense of scanning speed, or a monochrome-only chart system.

**3. FRAMA — warm, tactile material palette**  
https://framacph.com/  
Useful atmosphere reference: https://framacph.com/blogs/stores/frama-studio-store

- Borrow: natural warm neutrals, low-saturation surfaces, considered contrast between dark details and light space, understated materials.
- Translate to finance: a cream canvas, almost-white cards, muted olive highlights, carefully limited use of color.
- Do not borrow: product-photography dependence, low-contrast text, or a decorative physical texture behind financial data.

### Secondary references

**4. The Gentlewoman — strong editorial composition**  
https://thegentlewoman.co.uk/

- Borrow: bold decisions about alignment, purposeful emptiness, disciplined typographic hierarchy.
- Translate to finance: one memorable headline per view, with a clear and consistent visual order beneath it.
- Do not borrow: oversized experimental layouts for dense screens.

**5. Aesop — functional restraint in premium design**  
https://www.aesop.com/

- Borrow: disciplined labeling, quiet framing, compact but well-crafted controls, limited colors.
- Translate to finance: forms and settings that feel calm and deliberate.
- Do not borrow: an overly retail-oriented aesthetic or product-marketing copy.

**6. Norm Architects — balance, proportion, and composition**  
https://normcph.com/

- Borrow: balanced visual rhythm, careful proportions, restrained details and warmth.
- Translate to finance: harmonious card widths, comfortable margins, and measured density.
- Do not borrow: architectural portfolio layouts where images dominate the task.

### Reference priorities for implementation

| Decision | Primary inspiration | Practical rule |
| --- | --- | --- |
| Overall feel | reMarkable | Quiet, paper-like, focused |
| Typography and headings | Kinfolk | Editorial serif only where it serves hierarchy |
| Color temperature | FRAMA + softened Gruvbox Light | Cream and earth tones, no intense yellow cast |
| Composition | The Gentlewoman + Norm Architects | Intentional asymmetry, sound grid, generous section margins |
| Controls and forms | Aesop | Restrained but clearly interactive |
| Data tables and charts | Functional finance UI requirements | Legibility and precision take priority over all references |

**Instruction for Claude:** Inspect the linked references if web access is available. If not, use the descriptions above. Synthesize the shared visual principles rather than reproducing any one site's homepage. The app must still feel like trustworthy financial software. Use the specified tokens for consistency; do not try to extract colors/fonts automatically from changing reference websites.

## Design principles

1. **Paper, not beige decoration.** Use one dominant warm-paper background. Cards are subtly lighter, never heavily elevated.
2. **Editorial hierarchy, functional details.** Large, calm headlines may use a serif. Financial amounts, tables, controls, and chart labels always use a crisp sans-serif with tabular numerals.
3. **Space is structural.** Put generous space between conceptual sections and tighter, regular spacing within related data.
4. **Data comes first.** Charts and transactions must be scannable. Decorative styling must never hide values or their meaning.
5. **A neutral relationship with money.** Explain behavior without moralizing. An expense is not automatically a failure.
6. **Quiet interaction.** Few shadows, restrained motion, thin borders, clear focus states, and subdued hover colors.
7. **Accessible by default.** Never depend on color alone to convey a trend, category, or status.

## Color tokens

Use the **Warm Paper** palette as the default. These are design proposals, not extracted brand values.

| Token | Hex | Application |
| --- | --- | --- |
| `--color-bg` | `#F3EBDD` | Main app canvas, warm paper |
| `--color-surface` | `#FAF5EB` | Cards, panels, dialogs |
| `--color-surface-raised` | `#FFF9F0` | Select popovers and focused detail surfaces |
| `--color-ink` | `#302D29` | Main text, key numbers, icons |
| `--color-ink-muted` | `#71695E` | Secondary text and labels (slightly darker than initial moodboard for contrast) |
| `--color-ink-subtle` | `#837B70` | Large nonessential captions only; verify contrast |
| `--color-border` | `#D7CDBE` | Subtle separators and field outlines |
| `--color-accent` | `#657451` | Primary action / olive green |
| `--color-accent-hover` | `#4E5C3C` | Primary action hover |
| `--color-accent-soft` | `#E2E7D8` | Selected nav, understated accent surfaces |
| `--color-focus` | `#4D685C` | Focus ring |

### Supporting chart colors

| Purpose | Hex |
| --- | --- |
| Olive | `#657451` |
| Muted terracotta | `#A96F52` |
| Slate blue | `#647D89` |
| Ochre | `#B28B45` |
| Plum | `#8D7285` |
| Gray neutral | `#938A7D` |

These colors differentiate categories; do not assign moral judgments to them. For favorable/unfavorable comparisons, use wording, arrows, and signed values in addition to color. Check contrast for small text and chart marks; darken hues or add labels if needed.

### Usage proportions

Roughly 75–85% warm neutral canvas and surfaces; 10–20% charcoal text, lines, and data; 5% or less accent color outside charts. Avoid large saturated blocks.

## Typography

**Recommended readily available pairing:** `Source Serif 4` (variable, with optical sizes) for occasional display/editorial headings, `Inter` for all body, navigation, amounts, labels, and controls. Both are open-source (Google Fonts / Fontsource); bundle locally.

- Display headlines: Source Serif 4 Regular, `clamp(2.25rem, 1.75rem + 2vw, 3.25rem)` (36–52px), line-height `1.1`, tracking `-0.005em`. Use sparingly on overview and insight screens.
- Page headings: Inter Medium or Source Serif 4 Regular, `28–36px`, line-height `1.15–1.25`, normal tracking.
- Section headings: Inter Medium `18–22px`, line-height `1.3`.
- Body: Inter Regular `15–16px`, line-height `1.5–1.6`.
- Supporting text: Inter Regular `13–14px`, line-height `1.45`.
- Metadata/overlines: Inter Medium `11–12px`, tracking `0.06–0.1em`; uppercase only for short labels.
- Primary balances: Inter Regular/Medium `36–48px`, line-height `1.1`, tracking `-0.035em`, `font-variant-numeric: tabular-nums`.
- Table amounts: Inter Medium `14–16px`, tabular numerals, right-aligned.

Do not use a serif for dense data, transaction lists, navigation, buttons, or form input. Avoid ultra-light font weights. Use at most two families and 3–4 effective weights.

## Spacing and layout

- Base spacing step: `4px`; preferred tokens: `4, 8, 12, 16, 24, 32, 48, 64, 96`.
- Desktop max content width: `1200–1280px`, centered.
- Desktop outer gutters: `32–48px`; tablet `24–32px`; mobile `16–20px`.
- Major section separation: `48–72px` desktop, `32–48px` mobile.
- Card padding: `24–32px` desktop, `20–24px` mobile.
- Card-to-card gaps: `16–24px`.
- Label-to-value spacing: `4–8px`; item rows: `12–16px` vertically.
- Use an 8- or 12-column responsive grid; avoid leaving excessive blank space within dense analytical screens.
- Border radius: `12px` for cards, `8px` for fields, `999px` for pills; keep rounding consistent and restrained.
- Borders: `1px solid var(--color-border)` where needed. Avoid shadow stacks. Use at most a barely perceptible shadow for floating overlays.

## Components

### App shell

Warm paper background. Minimal navigation with an active state in soft olive. A fixed or sticky sidebar is acceptable on desktop; compact bottom navigation or a drawer on mobile. Keep navigation labels short and legible.

### Summary card

Small muted label, one prominent amount, concise period-over-period comparison. Avoid multiple competing hero numbers. Use tabular numerals and local currency formatting, e.g. `4 285,60 zł` for Polish locale.

### Insight card

Write a human observation first, quantitative evidence second, optional action third. Example: “Dining spending fell by 8% compared with last month.” Never substitute vague encouragement for actionable information. Mention comparison period and calculation basis.

### Transaction row

Merchant, date/category, right-aligned amount. Keep 44px minimum interactive target height; typically 56–72px per row. Distinguish income and outflow using signs, text, or icons, not just red/green. Use subtle dividers, not individually boxed rows.

### Charts

Use thin lines, calm colors, faint grid lines, minimal axes, and clear tooltip content. Prefer direct labels and concise legends. Keep multiple-series contrast distinguishable in grayscale where possible. Do not suppress zero baselines on bars. Use meaningful scales and honest comparisons. For dense charts, prioritize accuracy over decorative minimalism.

### Controls

Primary button: dark olive background with light readable text, medium weight, 10–12px radius, visible focus ring. Secondary button: transparent or warm surface with 1px border. Inputs: surface background, clear labels, visible boundaries, accessible validation messages. Hover states should be subtle but detectable.

## Motion and interaction

- Typical transitions: `120–200ms` for color, opacity, and small transforms.
- No distracting parallax or scroll-driven theatrics.
- Loading states: stable layout, subtle skeletons or progress indicators.
- Respect `prefers-reduced-motion`.
- Keyboard focus must be clearly visible, never removed.
- Maintain WCAG AA contrast targets for text and interactive controls; test actual color pairs, not palette colors in isolation.

## Voice and writing

Calm, factual, and concise. Prefer “You spent 12% less on groceries than last month” over “Great job! You're crushing your savings goals!” Clearly label estimates, averages, and incomplete transaction data. Never imply that positive/negative expense movement is inherently good/bad.

## Responsive behavior

On mobile, prioritize the total, period comparison, top insight, category breakdown, and recent transactions in that order. Stack cards into a single column. Tables may become transaction lists; retain accessible detail views. Charts must preserve readable labels and touch targets. Editorial whitespace scales down on small screens.

## What to avoid

- Pure-white canvas everywhere, cool fintech blue as a dominant color, saturated gradients.
- Heavy card shadows, glassmorphism, neon colors, oversized pill corners.
- Excessive serif text, tiny labels, low-contrast gray on cream.
- Huge empty areas that force users to scroll to see financial data.
- Decorative charts without axis context, unclear legends, or unexplained percentage changes.
- Treating every expense as “bad” via red styling.
- Copying reMarkable branding, logos, or proprietary visual assets.

## Implementation brief for Claude

> Implement the app in the **Warm Paper Finance** design system above. Treat the tokens and typography scale as source-of-truth. Create reusable CSS variables/theme tokens and reusable components rather than hardcoding styles per screen. Use Source Serif 4 for rare editorial headings and Inter for functional UI and tabular financial numerals. Preserve generous section spacing while keeping data-rich areas efficient. Use warm paper surfaces, charcoal text, subtle borders, olive primary actions, and muted chart colors. Make the app accessible, responsive, keyboard navigable, and precise about financial figures. Before completion, audit contrast, information density, numeric alignment, hover/focus states, and mobile layout. When existing project conventions conflict, adapt the tokens to its design system without introducing a parallel styling architecture.

## CSS starter tokens

```css
:root {
  --color-bg: #f3ebdd;
  --color-surface: #faf5eb;
  --color-surface-raised: #fff9f0;
  --color-ink: #302d29;
  --color-ink-muted: #71695e;
  --color-ink-subtle: #837b70;
  --color-border: #d7cdbe;
  --color-accent: #657451;
  --color-accent-hover: #4e5c3c;
  --color-accent-soft: #e2e7d8;
  --color-focus: #4d685c;
  --font-ui: 'Inter', system-ui, sans-serif;
  --font-editorial: 'Source Serif 4', Georgia, serif;
  --radius-card: 12px;
  --radius-control: 8px;
  --content-max: 1240px;
}
body { background: var(--color-bg); color: var(--color-ink); font-family: var(--font-ui); }
.numeric { font-variant-numeric: tabular-nums; }
```

## Implementation in this repository

### Where things live

| What | File |
| --- | --- |
| Tokens (colours, type scale, spacing, radii, motion), light + dark | `src/frontend/src/styles/_tokens.scss` |
| Element defaults and shared classes (`.display`, `.page-title`, `.section-title`, `.overline`, `.support`, `.numeric`, `.card`, `.button`, `.button--secondary`, `.field*`, `.visually-hidden`) | `src/frontend/src/styles/_base.scss` |
| Summary card, insight card, transaction row, category breakdown | `src/frontend/src/app/shared/ui/` |
| Money (`1 234,56 zł`, typographic minus, optional `+`) and date (`28.10.2026`) pipes | `src/frontend/src/app/shared/format/` |
| Living style guide on fictional data (dev builds only) | `/styleguide` → `src/frontend/src/app/features/styleguide/` |
| App icon and favicon (three category bars on paper); PNG/ICO are generated with `npm run icons` | `src/frontend/branding/*.svg`, `src/frontend/public/favicon.svg` |

Fonts are self-hosted from npm (`@fontsource-variable/inter`, `@fontsource-variable/source-serif-4`
with the optical-size axis; Latin Extended covers Polish diacritics), so the PWA works offline and makes no
third-party requests.

### Rules derived from the spec

- Components use only tokens; no raw hex values, font sizes or spacing in component styles.
- Polish UI text only through i18n; routes, identifiers and CSS classes stay English.
- Amounts: Inter, `tabular-nums`, right-aligned in lists. Expenses show `−` (U+2212), income `+`
  in lists. Income may use `--color-accent-text`; expenses never use red.
- Shares and changes are computed in code (components such as `SummaryCard` and
  `CategoryBreakdown` compute them from amounts); text only describes them.
- Comparisons are worded neutrally: "o 8% mniej niż we wrześniu", with an arrow icon and the
  previous amount, never only colour.

### Contrast audit (WCAG 2.x, light theme)

| Pair | Ratio | Use |
| --- | --- | --- |
| ink on bg / surface | 11.6 / 12.6 | all text |
| ink-muted on bg / surface | 4.6 / 5.0 | secondary text (AA) |
| ink-subtle on bg / surface | 3.5 / 3.8 | large captions only |
| accent on surface | 4.6 | olive fills, icons |
| accent on bg | **4.25** | not for small text on the canvas |
| accent on accent-soft | **3.99** | not for text: active nav uses ink text + olive icon |
| on-accent (`#FFF9F0`) on accent | 4.8 | primary button |
| accent-text (`#4E5C3C`) on surface / bg | 6.6 / 6.1 | links, income amounts |
| focus on bg | 5.1 | focus ring |

Chart ochre `#B28B45` is only 2.9:1 on surface, so `--chart-4` is darkened to `#A5803E` (3.4:1).
Every bar and dot also has a direct text label.

### Dark theme (not in the spec; derived proposal)

"Lamp-lit paper": warm charcoal instead of neutral black, the same roles, lighter olive and chart
hues. It follows `prefers-color-scheme`, and `data-theme="light|dark"` on `<html>` overrides it.

| Token | Dark |
| --- | --- |
| `--color-bg` / `surface` / `surface-raised` | `#1E1B17` / `#26221D` / `#2E2924` |
| `--color-ink` / `ink-muted` / `ink-subtle` | `#EDE5D8` / `#B0A696` / `#968C7E` |
| `--color-border` | `#3E3830` |
| `--color-accent` / `accent-hover` / `accent-soft` / `accent-text` | `#A9B88F` / `#BDCAA6` / `#353D2B` / `#A9B88F` |
| `--color-on-accent` / `focus` | `#1E1B17` / `#9EC2B0` |
| `--chart-1…6` | `#A9B88F` `#D29A7C` `#93AAB5` `#D2AE6C` `#B79AAE` `#B3AA9C` |

All dark text pairs are ≥ 4.5:1 (ink-subtle 4.8) and chart hues ≥ 6:1 on the surface.

### Additions to the spec

- `--color-accent-text` and `--color-on-accent` tokens (see audit).
- Insight cards have a 3 px olive left rule as their only accent; the headline uses the editorial
  serif at page-heading size.
- Mobile bottom navigation marks the active item with a soft olive pill behind the icon.

