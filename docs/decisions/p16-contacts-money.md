# p16 — The credit limit is money, and a gate holds every amount to its currency and rate

Date: 2026-10-25. Piece: p16-contacts. Status: accepted (round 1).

## Context

CLAUDE.md rule 2: every amount is stored with its currency, the exchange rate used and the
base-currency amount, in decimals. A contact's credit limit is the first amount any table holds
(the kernel's `Money` type and `MoneyMapping` existed, unused). Multi-currency (p08) is being built
beside this piece and is not merged, so there is no rate table to look a rate up in yet.

## Decision

1. **Six columns**, named as `MoneyMapping` names them: `credit_limit_amount numeric(28,10)`,
   `credit_limit_currency char(3)`, `credit_limit_exchange_rate numeric(19,8)`,
   `credit_limit_base_amount numeric(19,4)`, `credit_limit_base_currency char(3)`,
   `credit_limit_stated boolean`. The limit is optional (no limit is not the same as 0, which means
   no credit), so the six are nullable and a check constraint (`ck_contacts_credit_limit`) holds them
   all set or all null, the amount and base amount at least 0, the rate positive and 1 when the
   currency is the base currency. The entity maps them as plain columns and exposes a `Money?`
   built through `Money.Unrounded` (so the base amount is always the amount times the rate, rounded
   once per the owner-signed rounding rule); EF Core's optional complex types would do the same but
   bind poorly into the list framework's column expressions.
2. **The base currency** is the working company's base currency at the time the limit is set
   (`ICompanyDirectory.GetWorkingAsync`, AED for system work). `GET /api/contacts/contacts/defaults`
   tells the form which it is, so the form's money field shows the rate and the base amount for a
   limit in another currency. A limit saved again unchanged keeps the rate and base currency it was
   set with, whichever company the user works in now.
3. **The rate** is typed (base-currency units per unit) for a limit in another currency, and
   refused when missing (`contactsRateRequired`) or other than 1 in the base currency. When p08's
   rates are merged, the form can offer the day's rate as the default; the stored figures do not
   change shape.
4. **A stricter money gate** (`MoneyGateTests.Every_amount_column_carries_its_currency_rate_and_base_amount`,
   ratchet `rules.moneyAmountColumnsChecked`): every `{prefix}_amount` column of the database must be
   numeric and sit beside `{prefix}_currency`, `{prefix}_exchange_rate`, `{prefix}_base_amount` and
   `{prefix}_base_currency` of the right types, and a nullable one must be named, with all four, in a
   check constraint of its table. It covers every later module's amounts, not only this one.
5. **Screens and documents** show the amount with its currency (`format.amount`); the list's
   `creditLimit` column is a money column naming `creditLimitCurrency` as its currency (group totals
   per currency); decimals travel as strings.
