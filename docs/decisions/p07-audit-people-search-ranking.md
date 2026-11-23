# p07-audit: ranking of the "Changed by" person search

Decision: IUserDirectory.SearchAsync (additive optional `language` parameter) ranks matches in one query:
a name or e-mail equal to the text first, then names/e-mails that start with it, then the rest; each group
ordered by the name in the user's language (Arabic: DisplayNameAr, else DisplayName), then id.
Arabic names are matched with a PostgreSQL regular expression built by Identity's NameMatch (alef forms,
ya/alef maqsura, ta marbuta/ha are one letter; diacritics and tatweel between letters ignored; runs of spaces equal).
Reason: the earlier order (English display name only) put the exact person below the 10 options the picker asks for.
One query keeps total and paging consistent; the match is a scan like the earlier ILIKE '%x%', so its cost class is unchanged.
The audit endpoint's e-mail-hiding filter folds names the same way so it does not drop letter-form matches.
Tests: AuditTrailTests.The_changed_by_picker_ranks_the_exact_name_first_then_names_that_start_with_it_in_either_language.
Not done: debounce of the picker's per-keystroke request.
