# Figures that never disagree between modules

Date: 2026-11-22. Owner's decision (gauntlet/needs-human.md, the row of 2026-11-22 on cross-module
figures). Input to the accounting module's plan.

## 1. Design that prevents conflicts

- The general ledger is the only source of truth for money. Sales, purchasing, stock, payroll and
  every later module post through the accounting module's public contract; none writes ledger
  tables (CLAUDE.md: modules use each other only through public contracts and events).
- Every posting traces back to the source document that caused it (CLAUDE.md rule 3).
- Control accounts (receivables, payables, inventory, VAT, bank clearing) accept no manual
  journal; only their own module posts to them.
- Posted entries are never edited or deleted, only reversed.

## 2. Deterministic reconciliation checks, every night

A background job (p12) runs these checks for every company and stores the result:

| Check | Compares |
|---|---|
| Receivables | The sum of customer balances with the receivables control account |
| Payables | The sum of supplier balances with the payables control account |
| Stock | The stock valuation with the inventory account |
| VAT | The VAT report with the VAT accounts, by period |
| Bank | The bank statement balance with the bank account |
| Sales | Posted invoices and credit notes with the revenue accounts |
| Payroll | The payroll run with salary expense and payable accounts |

A difference appears on a dashboard, in English and Arabic, and drills down to the documents and
postings that cause it. Each check has tests and golden scenarios, including a planted difference
that it must catch. A check never changes data by itself.

## 3. AI later, as a helper only

With the AI module (owner's direction of 2026-11-22), AI may explain a difference in plain English or
Arabic, suggest its likely cause and a fix for a person to confirm, and flag unusual patterns that
no rule covers. It is never the check itself and never posts anything.
