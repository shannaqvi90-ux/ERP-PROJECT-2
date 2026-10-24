using Erp.Kernel.Localization;
using Erp.Modules.Numbering.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Numbering;

/// <summary>
/// The kinds of document a UAE trading or manufacturing company numbers, registered by the platform
/// until the modules that make them exist (sales, purchasing, inventory, payments, accounting); each
/// of those modules then registers its own with
/// <see cref="NumberingRegistration.AddNumberedDocumentType"/> and this list loses the entry. The
/// defaults are the product's: a short prefix with the year, five digits, a new run each year (each
/// month for journal vouchers), and the tax documents and journal vouchers gapless. Whether the law
/// requires tax invoices and credit notes to run without gaps is a statutory question for the owner
/// (gauntlet human gate); gapless is the default because it is the stricter choice.
/// </summary>
internal static class StandardDocumentTypes
{
    private static NumberedDocumentType Type(string key, string prefix, bool gapless = false, string reset = NumberReset.Yearly, int padding = 5) =>
        new(key, $"numbering.documentType.{key}", prefix, padding, reset, gapless);

    public static readonly IReadOnlyList<NumberedDocumentType> All =
    [
        Type("sales.quotation", "QT-{YYYY}-"),
        Type("sales.order", "SO-{YYYY}-"),
        Type("sales.invoice", "INV-{YYYY}-", gapless: true),
        Type("sales.creditNote", "CN-{YYYY}-", gapless: true),
        Type("purchasing.order", "PO-{YYYY}-"),
        Type("purchasing.bill", "BILL-{YYYY}-"),
        Type("inventory.receipt", "GRN-{YYYY}-"),
        Type("inventory.delivery", "DN-{YYYY}-"),
        Type("payments.receipt", "RV-{YYYY}-"),
        Type("payments.payment", "PV-{YYYY}-"),
        Type("accounting.journal", "JV-{YYYY}-{MM}-", gapless: true, reset: NumberReset.Monthly, padding: 4),
    ];
}

/// <summary>The registered document types, in registration order, with their names in both
/// languages. The names are read from the string catalogue only when asked for (seeding needs only
/// the keys and defaults); a module test holds every registered type to a name in English and Arabic.</summary>
internal sealed class DocumentTypeCatalog
{
    private readonly Dictionary<string, NumberedDocumentType> _types = new(StringComparer.Ordinal);
    private readonly IServiceProvider _services;

    public DocumentTypeCatalog(IEnumerable<NumberedDocumentType> types, IServiceProvider services)
    {
        _services = services;
        foreach (var type in types)
        {
            var problems = type.Problems().ToList();
            if (!_types.TryAdd(type.Key, type))
            {
                problems.Add($"numbered document type '{type.Key}' is registered twice");
            }
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(string.Join("\n", problems));
            }
        }
    }

    public IEnumerable<NumberedDocumentType> All => _types.Values;

    public NumberedDocumentType? Find(string key) => _types.GetValueOrDefault(key);

    public string Name(NumberedDocumentType type, string language) => _services.GetRequiredService<StringCatalog>().Get(type.LabelKey, language);

    public string Name(string key, string language) => Find(key) is { } type ? Name(type, language) : key;
}

/// <summary>Makes the sequences a company lacks, one per registered document type, from the types'
/// defaults: for every company when a workspace is seeded, and for a new company when it is created.</summary>
internal sealed class DefaultSequences(NumberingDbContext db, DocumentTypeCatalog types)
{
    public async Task<int> AddMissingAsync(IReadOnlyCollection<Guid> companyIds, CancellationToken cancellationToken)
    {
        if (companyIds.Count == 0)
        {
            return 0;
        }
        var held = await db.Sequences.AsNoTracking().Where(s => companyIds.Contains(s.CompanyId))
            .Select(s => new { s.CompanyId, s.DocumentType }).ToListAsync(cancellationToken);
        var have = held.Select(h => (h.CompanyId, h.DocumentType)).ToHashSet();
        var added = 0;
        foreach (var company in companyIds)
        {
            foreach (var type in types.All.Where(t => !have.Contains((company, t.Key))))
            {
                db.Sequences.Add(FromDefaults(company, type));
                added++;
            }
        }
        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        return added;
    }

    public static NumberSequence FromDefaults(Guid companyId, NumberedDocumentType type) => new()
    {
        CompanyId = companyId,
        DocumentType = type.Key,
        Prefix = type.Prefix,
        Suffix = type.Suffix,
        Padding = type.Padding,
        Reset = type.Reset,
        Gapless = type.Gapless,
        PerBranch = false,
    };
}

/// <summary>A new company gets a sequence for every document type, in the transaction that creates it.</summary>
internal sealed class NewCompanySequences(DefaultSequences defaults) : Tenancy.Contracts.ICompanyCreatedHandler
{
    public async Task CompanyCreatedAsync(Tenancy.Contracts.CompanyInfo company, CancellationToken cancellationToken) =>
        await defaults.AddMissingAsync([company.Id], cancellationToken);
}
