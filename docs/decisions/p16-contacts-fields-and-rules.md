# p16 — Contact fields and the rules applied to them

Date: 2026-10-25. Piece: p16-contacts. Status: accepted (round 1).

## Fields

Kind (organisation or person), code (shown as Reference, unique in the workspace when given),
names in English and Arabic (at least one), the organisation a person belongs to and their job
title, e-mail, mobile and phone, website, address (two lines, area, city, emirate, PO box, country,
the address in Arabic), tags, trade licence number, tax registration number, the currency the
contact trades in, an optional credit limit (money, see p16-contacts-money.md), notes.

## Rules, and where each comes from

- **Tax registration number**: exactly 15 digits once spaces and dashes are removed, Arabic-Indic
  digits turned into 0 to 9 — the owner-signed rule `docs/compliance/vat-trn-format.md` (FTA/MoF
  source recorded there). No prefix or check digit. The database holds the same line
  (`ck_contacts_tax_registration_number`).
- **Trade licence number**: a plain field (at most 50 characters). Licence formats differ by
  issuing authority and no official source gives one; none is applied (piece spec, human gate).
- **Phone numbers**: international form. The shape (digits, spaces, brackets, hyphens, optionally a
  leading +) and, for a number written with the UAE's code, the lengths of the UAE numbering plan:
  the same lengths the kernel's phone field completes on (`web/src/kernel/forms/phone.ts`) and the
  company form checks, recorded with their source (ITU-T E.164 plan for country code 971) in
  `p06-form-report-whole-documents.md`. This is a telecommunications numbering fact, not a tax,
  payroll or statutory rule. The form's phone field keeps +971 and takes the national number.
- **E-mail**: the kernel's check. **Website**: a web address; `www.example.ae` is saved as
  `https://www.example.ae`. **Country**: ISO 3166-1 alpha-2. **Emirate**: one of the seven, only for
  an address in the UAE. **Currency**: a current ISO 4217 code with minor units (the kernel's list).
- **Tags**: up to 10, each up to 40 characters, stored as one text joined by "; " in the order typed
  (case-insensitive duplicates dropped), so the list filters them with `contains` and shows them as
  typed. A tag table of their own is left until a module needs tag colours or tag rights.
- **Code**: up to 30 letters, digits, dots, slashes, underscores or hyphens. It is named `code` in the
  API, like a company's or a branch's code; the screens call it Reference.

## Duplicate warning

`GET /api/contacts/contacts/duplicates?sameName=…&sameNameAr=…&sameEmail=…&samePhone=…&sameMobile=…&sameTaxNumber=…&except=…` (read permission) answers up to 10 contacts of the workspace
that share with what is being typed: the name in English or Arabic (lower case, punctuation and
spacing ignored, the Arabic spellings people type for one another folded: أ إ آ ٱ → ا, ى → ي, ة → ه,
tatweel and short vowels dropped), the e-mail (case ignored), a phone or mobile number however it was
written (+971 50…, 00971 50…, 050…, 50… all compare as 97150…), or the tax registration number. The
compared forms are stored beside the record (`name_key`, `name_ar_key`, `email_key`, `phone_key`,
`mobile_key`, each with a `(tenant_id, key)` index), so the check is a handful of index lookups at
any volume (measured in `ContactsListVolumeTests`). It is a warning, never a refusal: two contacts
may share a phone number (a switchboard) or a name. The form asks 400 ms after the last key and
names each match with what it shares, each opening on a click; row-level security keeps the check
inside the caller's workspace (a test sends another workspace's exact name, e-mail and TRN and gets
nothing).

## Arabic

Every server message (validation, permission labels) and every screen string has its Arabic twin;
on an Arabic screen the Arabic name comes first and a new contact starts there; the contact card
prints in Arabic with the Arabic names and address (the reports module's shaping).
