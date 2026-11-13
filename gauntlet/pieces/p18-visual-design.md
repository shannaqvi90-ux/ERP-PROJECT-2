# p18 — Visual design system, in the Microsoft Dynamics 365 manner

Added by the owner on 2026-11-13 (needs-human #47): "design odoo se behtar hona chahiye,
microsoft dynamics jesa" (the design must be better than Odoo's, like Microsoft Dynamics).
It comes before wave 3's screens so that every later screen is built on it.

Two stages, because the owner chooses the look (owner, 2026-11-13: "mujhay best design chahiye. to
mujhay samples dikhana jab design pe kaam karo"):
1. Samples first. Before changing any screen, the builder makes three distinct design directions
   in the Dynamics 365 manner, each shown on the same screens with real demo data: the contacts
   list with a contact open, a contact form, a settings list, and the sign-in screen; each in
   English and Arabic, light and dark. Each direction comes as static pages under
   docs/design/samples/<direction>/ built only from that direction's design tokens, with
   screenshots, and a one-page note of its palette, type, spacing and component choices. The lead
   shows them to the owner. This stage is a human gate: it ends when the owner picks a direction
   (or asks for changes) in needs-human; nothing is built into the product before that.
2. Build. The chosen direction becomes the design system below, across every screen.

- One design system for the whole product, used by every module through the shared kernel
  components: design tokens (colour, type scale, spacing grid, radius, elevation, motion) in one
  place; light and dark themes; a high-contrast mode; English and Arabic (right-to-left) from the
  same tokens.
- The look and the working style of Dynamics 365 business screens, built by us: a command bar of
  the record's actions with icons and an overflow menu, a clear page header (title, status,
  key facts), a site map / navigation with icons and collapsible groups, dense but calm grids
  with sticky headers, row hover and selection, column resizing and no text cut off without a
  tooltip, tabbed and sectioned forms with a consistent label/field grid, a details pane, toasts
  and inline validation, empty states, loading skeletons.
- Our own components for what the browser draws plainly today: drop-down and look-up fields with
  search, a date and date-range picker, a number and money field that shows the currency, a
  tag field, a file drop zone, dialogs, menus and tooltips, all keyboard-first and right-to-left.
- Locale formats everywhere: dates as dd/mm/yyyy (and the Arabic form in Arabic), numbers and
  amounts with the locale's separators, the currency beside every amount; nothing American by
  default.
- Typography: one well-made type family for Latin and Arabic with a clear scale, bundled with the
  product so screens and printed documents look the same on every machine. Fonts under the SIL
  Open Font License are approved by the owner (needs-human #5, 2026-10-03; Noto Sans Arabic is
  already bundled); choose a Latin face that pairs with the Arabic one, and keep the font a single
  token. Icons: one consistent icon set under an allowed licence (MIT, Apache-2.0, BSD, or ISC,
  which the owner approved in needs-human #3; for example Fluent UI System Icons, MIT). Components
  may come from an MIT-licensed library such as Fluent UI React, or be our own.
- Never copy Microsoft's or Odoo's logos, product names, screenshots, artwork or text (CLAUDE.md
  rule 7 applies to Odoo; Microsoft's trademarks are not ours either): the manner, not the brand.
- Accessibility: WCAG 2.1 AA contrast in both themes, visible focus, every control reachable and
  operable by keyboard, screen-reader labels in both languages.
- Fix what reviewers saw: dates shown as mm/dd/yyyy, the attachments table's Delete buttons cut to
  'De', the credit-limit currency cut to 'AEC', names cut in lists with no way to read them.
- No change to what a screen does: every existing test, gate, keyboard path and comparison result
  must still pass or improve; the keystroke and step counts against Odoo may not get worse.

Compared against Odoo: the critic takes the same screens in both products (a list of 100,000
contacts, a contact form, the settings screens, in English and Arabic, light and dark) and judges
them side by side against a written checklist (consistent spacing and alignment, type hierarchy,
clear primary action, icons, no clipped text, locale formats, contrast, empty and loading states,
right-to-left correctness), with screenshots; ours must be at least as good on every item and
better overall, and every existing comparison task must not get worse.
