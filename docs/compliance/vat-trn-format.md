# Tax Registration Number (TRN) format

Checked: 2026-10-10. Signed off: the owner, 2026-10-10 (needs-human #10). Owning piece: p02-tenancy.

## Rule

- A TRN is exactly 15 digits, 0 to 9, nothing else. Spaces and dashes a user types are removed
  before the check; anything else is refused.
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
