using System.Globalization;
using Erp.Kernel.ImportExport;
using Erp.Kernel.Money;
using Erp.Modules.Currency.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Contacts;

/// <summary>
/// The contacts a file's rows may be the same as (p14): found the way the contact form warns of duplicates (an equal name, e-mail, phone
/// number or tax registration number, plus an equal reference), read once for a whole batch of rows instead of once per row.
/// </summary>
internal sealed class DuplicateIndex
{
    internal sealed record Known(Guid Id, string? Code, string NameKey, string NameArKey, string? EmailKey, string? PhoneKey, string? MobileKey, string? Trn);

    private readonly List<Known> _known;

    private DuplicateIndex(List<Known> known) => _known = known;

    private sealed record Probe(string? Code, string? Name, string? NameAr, string? Email, IReadOnlyList<string> Phones, string? Trn);

    private static Probe ProbeOf(SaveContactRequest request)
    {
        var name = ContactsValidation.NameKey(request.NameEn);
        var nameAr = ContactsValidation.NameKey(request.NameAr);
        return new Probe(ContactsValidation.Clean(request.Code), name.Length >= 3 ? name : null, nameAr.Length >= 3 ? nameAr : null, ContactsValidation.EmailKey(request.Email),
            new[] { ContactsValidation.PhoneKey(request.Phone), ContactsValidation.PhoneKey(request.Mobile) }.Where(p => p is { Length: >= 4 + 7 }).Select(p => p!).Distinct().ToList(),
            ContactsValidation.NormalizeTaxNumber(request.TaxRegistrationNumber) is { Length: 15 } digits && digits.All(char.IsAsciiDigit) ? digits : null);
    }

    public static async Task<DuplicateIndex> LoadAsync(ContactsDbContext db, IReadOnlyList<ImportRow> rows, string baseCurrency, CancellationToken cancellationToken)
    {
        var probes = rows.Select(r => ProbeOf(ContactsImport.Build(r.Values, baseCurrency, null))).ToList();
        var codes = probes.Select(p => p.Code).Where(c => c is not null).Select(c => c!).Distinct().ToList();
        var names = probes.SelectMany(p => new[] { p.Name, p.NameAr }).Where(n => n is not null).Select(n => n!).Distinct().ToList();
        var emails = probes.Select(p => p.Email).Where(e => e is not null).Select(e => e!).Distinct().ToList();
        var phones = probes.SelectMany(p => p.Phones).Distinct().ToList();
        var trns = probes.Select(p => p.Trn).Where(t => t is not null).Select(t => t!).Distinct().ToList();
        if (names.Count + emails.Count + phones.Count + trns.Count + codes.Count == 0)
        {
            return new DuplicateIndex([]);
        }
        var known = await db.Contacts.AsNoTracking()
            .Where(c => (c.Code != null && codes.Contains(c.Code)) || names.Contains(c.NameKey) || names.Contains(c.NameArKey) || (c.EmailKey != null && emails.Contains(c.EmailKey)) ||
                        (c.PhoneKey != null && phones.Contains(c.PhoneKey)) || (c.MobileKey != null && phones.Contains(c.MobileKey)) ||
                        (c.TaxRegistrationNumber != null && trns.Contains(c.TaxRegistrationNumber)))
            .Select(c => new Known(c.Id, c.Code, c.NameKey, c.NameArKey, c.EmailKey, c.PhoneKey, c.MobileKey, c.TaxRegistrationNumber))
            .ToListAsync(cancellationToken);
        return new DuplicateIndex(known);
    }

    /// <summary>The contacts the row matches on any of the five things.</summary>
    public IReadOnlyList<Known> Find(SaveContactRequest request)
    {
        var p = ProbeOf(request);
        return [.. _known.Where(k => Matches(k, p, byName: true, byEmail: true, byPhone: true, byTrn: true, byCode: true))];
    }

    private static bool Matches(Known k, Probe p, bool byName, bool byEmail, bool byPhone, bool byTrn, bool byCode) =>
        (byCode && p.Code is not null && string.Equals(k.Code, p.Code, StringComparison.OrdinalIgnoreCase)) ||
        (byTrn && p.Trn is not null && k.Trn == p.Trn) ||
        (byEmail && p.Email is not null && k.EmailKey == p.Email) ||
        (byPhone && p.Phones.Count > 0 && ((k.PhoneKey is not null && p.Phones.Contains(k.PhoneKey)) || (k.MobileKey is not null && p.Phones.Contains(k.MobileKey)))) ||
        (byName && ((p.Name is not null && (k.NameKey == p.Name || k.NameArKey == p.Name)) || (p.NameAr is not null && (k.NameArKey == p.NameAr || k.NameKey == p.NameAr))));

    /// <summary>The one contact an update changes: the one with the row's reference, else its tax registration number, e-mail, name, or phone number,
    /// in that order of trust; when the best of these matches more than one contact, none (the row is refused as ambiguous).</summary>
    public (Guid? Target, string? Problem) Target(SaveContactRequest request, IReadOnlyList<Known> matches)
    {
        var p = ProbeOf(request);
        foreach (var (byCode, byTrn, byEmail, byName, byPhone) in new[] { (true, false, false, false, false), (false, true, false, false, false), (false, false, true, false, false), (false, false, false, true, false), (false, false, false, false, true) })
        {
            var level = matches.Where(k => Matches(k, p, byName, byEmail, byPhone, byTrn, byCode)).ToList();
            if (level.Count == 1)
            {
                return (level[0].Id, null);
            }
            if (level.Count > 1)
            {
                return (null, "contactsImportAmbiguous");
            }
        }
        return (null, null);
    }
}

