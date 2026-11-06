# p09 — Comparison task: add a custom field and filter by it

Date: 2026-11-06. Piece: p09-custom-fields, round 1. Status: accepted.

Task: `gauntlet/compare/tasks/custom-field-filter.mjs`; ours driver
`gauntlet/compare/drivers/ours/custom-field-filter.mjs` (built).

## Our shortest path

Custom fields (navigation) > New custom field > type the label (the cursor is there; a new field is
Text and searched by the list's search box; the Arabic label may stay blank) > Enter > Contacts
(navigation; the search box has the focus) > type the contact's name > Enter > click the new field >
type the value > Ctrl+S > "/" (the search box, text chosen) > type the value: exactly that contact.

## Paths tried

- Filter through the column chooser and a column's Filter menu: five more actions than the search.
  Both are offered (the field is a filterable column); the driver takes the search because a text
  field is searched by the same box as the contact's own fields.
- Defining the field from the contact's form, as Odoo does: not offered. The field is a setting of the
  workspace (every company shares it) and has its own screen under the permission that guards it;
  putting the definition behind a contact's form would offer a workspace-wide change from a record
  screen that has only the record's permission.
- Making the field's value a column of the contacts list at once: the column is hidden until chosen
  (a workspace with 40 fields would otherwise bury the list).
- Typing the Arabic label: left optional (a blank Arabic label shows the English one), so the path
  has no Arabic keystrokes.

If a metric ties, why ours cannot be shorter: the label and the value must be typed once each, the
contact must be found, and the filter value must be typed once more; the three clicks and one chord
that remain are navigation to two screens and the field on the form.

## Set-up and clean-up

Set-up switches off any earlier copy of the field named "Licence ref" (a field is switched off, never
deleted; the 40-field cap counts fields in use only). Clean-up switches off the one the run added.
