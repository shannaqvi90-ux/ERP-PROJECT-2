# Custom field defaults are built from the field's type (round 5)

A field's default is stored as text. Applying it on a save (a new record without the key, an import row, an old record whose field became required) must give the parser the JSON the field's type expects. A yes/no default was passed as the JSON string "true", which the boolean parser refuses, so API creates and imports failed and old records could not be saved. All three paths now use `JsonOfText` (boolean becomes JSON true or false, other types stay text). Tests cover each type (text, number, date, select, yes/no true and false) for a new record without the key, an old record with the field required, and an import row.

The audit log, its print and export show a custom number with the field's own decimals (rounded half away from zero in decimal arithmetic), not the eight the column stores. The native credit limit has no field label and belongs to p07-audit.

Cost: no added processor time beyond about 8 small tests.
