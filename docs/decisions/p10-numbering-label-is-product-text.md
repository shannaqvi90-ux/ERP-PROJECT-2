# Numbering: the audit label holds the document type only

Decision (round 7). numbering.sequences.name_en / name_ar (copied to counters by trigger) held
"<document type> · <company legal name>". That stored tenant-entered text a second time in a new column; the
G1 HTTP gate, which writes API example values into both tenants, read tenant B's label as a B marker and
reported 77+ false leaks.

The label is needed as a stored column (audit.capture names a record from its own name columns, and the
counters trigger copies it), so it cannot be derived at read time inside the audit log. Instead it now holds
product text only: the document type's name in English and Arabic. The company is already a column of the
sequence and is shown by the sequences list and page, so nothing is lost for the screens; the history heading
of a sequence no longer names its company (record_code and the record link identify the row).
Existing rows keep their old label until saved; the seeder only fills empty ones.
