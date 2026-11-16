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

The owner's direction (2026-11-15, on the lead's proposal): the power of Dynamics 365 in a modern,
clean manner; better than Dynamics where it is heavy, slow, grey or weak in Arabic. Principles every
direction keeps:
- Dense but calm: much on one screen, with clear spacing and order; a density switch the user sets
  (Compact for more rows, Comfortable for more air), kept per user.
- Fast: every action by keyboard, no waiting for screens that could already be drawn.
- One clear main action per screen (Save, New invoice), the rest quieter.
- Numbers and money easy to read: tabular figures aligned, the currency beside every amount,
  negative amounts in red or brackets as accountants expect.
- Arabic as good as English from the first screen: a fine Arabic face, full right to left.
- Light, dark and high-contrast themes.
- Dashboards that say what needs doing, not only figures; empty states that guide to the first step.
- Our own brand: the product is named Bizora ERP (owner, 2026-11-15, needs-human #1). The owner
  chose the logo: a wordmark only, plus an app icon made from its b (docs/design/brand/, vector SVG;
  ink #09071F, violet accent #6A00E8 light / #9B5CFF dark). Every sample direction uses these files
  as they are, with the matching Arabic wordmark file beside or under it; the directions differ in
  everything else. Never Microsoft's or Odoo's marks.

The three sample directions (all dense and keyboard-first; they differ in look and feel):
- A, close to Dynamics: the familiar business look in the Fluent manner, cleaner and faster.
- B, modern and clean: fewer lines, more space, quick and light, like the newer software-as-a-service
  tools.
- C, calm and premium: soft colours, the best Arabic typography, a light touch of the UAE in colour
  or pattern (no pictures), the feel of high-end software.
The owner may pick one or combine them (for example A's layout with C's colours).

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
