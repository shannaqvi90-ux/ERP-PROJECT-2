# p00 — ICU-style plural messages in every string file

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

Strings use a small subset of ICU MessageFormat on both sides: `{name}` placeholders and
`{count, plural, =0 {…} one {# item} other {# items}}`. The plural category comes from the
Unicode CLDR plural rules: on the web through the browser's `Intl.PluralRules`, on the server
from `Erp.Kernel.Localization.PluralRules` (English one/other; Arabic zero/one/two/few/many/other,
few = n mod 100 in 3..10, many = n mod 100 in 11..99). Numbers in messages use Western digits
with comma grouping in both languages, as the web client already does for ar-AE.

The string gates (`web/scripts/check-strings.mjs` and `StringGateTests`) require English and
Arabic texts of a key to use the same placeholders, every plural message to cover the categories
its language needs, and every `*.count` key to be a plural message.

## Why

Arabic has six plural forms; "{count} مستخدم" is wrong for 2 and for 3 to 10, and English read
"1 users". A full ICU library (FormatJS on the web, a .NET port on the server) would add
dependencies for a feature we use in a few dozen strings; the subset is about 150 lines per side,
has no dependencies and is tested against the CLDR categories. If select/ordinal formats become
necessary, adopt `@formatjs/intl-messageformat` (MIT) on the web and extend the server formatter.
