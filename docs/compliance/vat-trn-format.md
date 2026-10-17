# Tax Registration Number (TRN) format

Checked: 2026-10-10. Signed off: the owner, 2026-10-10 (needs-human #10). Owning piece: p02-tenancy.

## Rule

- A TRN is exactly 15 digits, 0 to 9, nothing else. Spaces and dashes a user types are removed
  before the check; anything else is refused.
- Arabic-Indic digits typed on an Arabic keyboard (٠ to ٩, U+0660 to U+0669) are turned into 0 to 9
  before the check (owner decision 2026-10-17, below). The TRN is stored, and shown on screens and
  printed documents in both languages, in the digits 0 to 9 only.
- The Tax Identification Number (TIN) is the first 10 digits of the TRN. It is derived from the
  TRN, never entered separately.
- No prefix rule (for example "starts with 100") and no check-digit rule: no official publication
  found states either. Some private websites claim them; they are not a source.
- Checking a TRN against the FTA's online verification service is outside this repository and
  needs the owner's approval first.

## Source

UAE Ministry of Finance, *UAE Electronic Invoicing Guidelines*, Version 1.1, 1 June 2026,
definitions:
<https://mof.gov.ae/wp-content/uploads/2026/06/UAE-Electronic-Invoicing-Guidelines_V-1.1-01June2026.pdf>

> Tax Identification Number ("TIN"): A unique 10-digit identifier and the first 10 digits of the
> 15-digit TRN issued to all entities registered with FTA.

> The TIN is the first 10 digits of the TRN that you have been issued.

## Product today (2026-10-10)

`TenancyValidation.TaxNumberPattern` is `^[0-9]{1,20}$` and the column allows 20 characters, so
'12345' is accepted (raised by the p06 r3 critic). p02 changes the check to exactly 15 digits,
with English and Arabic messages, and deals with stored values that do not match.

## Owner decision, 2026-10-17 (needs-human #18)

Question (raised by the p02 r10 builder): should Arabic-Indic digits (٠ to ٩) typed on an Arabic
keyboard be turned into 0 to 9 before the 15-digit check, or refused?

Owner's words: "han TRN english main hona chahiye" (yes, the TRN should be in English digits).

Recorded as: convert ٠ to ٩ into 0 to 9 before the check; keep and show the TRN in 0 to 9 only.
This is an input rule, not a statutory one: the official format above (15 digits) is unchanged.
Other digit sets (for example the Persian ۰ to ۹) were not part of the question and stay refused.
