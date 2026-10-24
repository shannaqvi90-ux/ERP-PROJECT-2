using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Shell;
using Erp.Modules.Numbering.Contracts;
using Erp.Modules.Numbering.Seeding;
using Erp.Modules.Numbering.Sequences;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Numbering;

/// <summary>
/// Document numbering: for every company, one sequence per kind of document (purchase order, tax
/// invoice …) with its pattern (prefix, digits, suffix, the year, month and branch tokens), when it
/// starts again (never, every year, every month), whether it may skip numbers, and its counters per
/// period and branch; the next number previewed; and <see cref="IDocumentNumbers"/>, which later
/// modules call to take a number inside the transaction that saves their document. Every issued
/// number is recorded with its document.
/// </summary>
public sealed class NumberingModule : ErpModule
{
    public override string Name => "numbering";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. NumberingPermissions.All]);
        module.DbContext<NumberingDbContext>();
        foreach (var type in StandardDocumentTypes.All)
        {
            module.Services.AddNumberedDocumentType(type);
        }
        module.Services.AddScoped<DocumentTypeCatalog>();
        module.Services.AddScoped<DefaultSequences>();
        module.Services.AddScoped<IDocumentNumbers, DocumentNumbers>();
        module.Services.AddScoped<ICompanyCreatedHandler, NewCompanySequences>();
        module.Endpoints(SequenceEndpoints.Map);
        module.Menu(new MenuEntry("numbering.sequences", "numbering.menu.sequences", "/numbering/sequences", NumberingPermissions.SequencesRead, Order: 860, Group: "settings"));
        module.List(SequencesList.Create());
        // Reports print the list exactly as the screen shows it.
        module.ListRows(SequencesList.Key, async (services, request, http, cancellationToken) =>
            (await SequenceEndpoints.PageAsync(services.GetRequiredService<NumberingDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.Seeder<NumberingSeeder>();
    }
}

/// <summary>
/// How one company numbers one kind of document. Every company holds one for each registered
/// document type (made with the company, from the type's defaults). Every branch of the company
/// shares it (<see cref="ICompanyWide"/>): only someone who works in every branch changes it.
/// </summary>
public sealed class NumberSequence : TenantEntity, ICompanyWide
{
    public Guid CompanyId { get; set; }

    /// <summary>A registered <see cref="NumberedDocumentType.Key"/>.</summary>
    public string DocumentType { get; set; } = "";

    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";

    /// <summary>Digits of the counter, with leading zeros.</summary>
    public int Padding { get; set; } = 5;

    /// <summary><see cref="NumberReset"/>.</summary>
    public string Reset { get; set; } = NumberReset.Yearly;

    /// <summary>Numbers are taken inside the document's transaction and never skip.</summary>
    public bool Gapless { get; set; }

    /// <summary>Each branch counts on its own (the pattern then holds {BRANCH}).</summary>
    public bool PerBranch { get; set; }
}

/// <summary>
/// The counter of one sequence in one period ("" when it never starts again, "2026", "2026-10") and,
/// for a sequence counted per branch, one branch. Any user who saves a document of the company writes
/// it, so it is company data but not shared-record data.
/// </summary>
public sealed class NumberCounter : TenantEntity, ICompanyOwned
{
    public Guid CompanyId { get; set; }
    public Guid SequenceId { get; set; }

    /// <summary>The branch counted; null for a sequence the whole company counts.</summary>
    public Guid? BranchId { get; set; }

    public string Period { get; set; } = "";

    /// <summary>The value the next number gets.</summary>
    public long NextValue { get; set; } = 1;

    /// <summary>The last value given (null: none yet in this period).</summary>
    public long? LastIssued { get; set; }

    public DateTimeOffset? LastIssuedAt { get; set; }
}

/// <summary>
/// A number given to a document: the record that a number exists, which document it went to, and who
/// took it when (the audit trail). Unique per company and document type, so a pattern changed later
/// can never give the same number twice.
/// </summary>
public sealed class IssuedDocumentNumber : TenantEntity, ICompanyOwned
{
    public Guid CompanyId { get; set; }
    public Guid SequenceId { get; set; }
    public string DocumentType { get; set; } = "";
    public Guid? BranchId { get; set; }
    public string Period { get; set; } = "";
    public long Value { get; set; }
    public string Number { get; set; } = "";
    public DateOnly DocumentDate { get; set; }

    /// <summary>The document the number was given to, when the module said.</summary>
    public Guid? SourceId { get; set; }

    public bool Gapless { get; set; }
}

public sealed class NumberingDbContext(DbContextOptions<NumberingDbContext> options, ITenantContext? tenant = null) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "numbering";

    /// <summary>Longest document type key, period and printed number the columns hold.</summary>
    public const int DocumentTypeLength = 72;
    public const int PeriodLength = 7;
    public const int NumberLength = 120;

    protected override string Schema => SchemaName;

    public DbSet<NumberSequence> Sequences => Set<NumberSequence>();
    public DbSet<NumberCounter> Counters => Set<NumberCounter>();
    public DbSet<IssuedDocumentNumber> Issued => Set<IssuedDocumentNumber>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NumberSequence>(e =>
        {
            e.ToTable("sequences", t =>
            {
                t.HasCheckConstraint("ck_sequences_padding", $"padding BETWEEN {NumberPattern.MinPadding} AND {NumberPattern.MaxPadding}");
                t.HasCheckConstraint("ck_sequences_reset", "reset IN ('never', 'yearly', 'monthly')");
                t.HasCheckConstraint("ck_sequences_document_type", "document_type ~ '^[a-z][a-zA-Z0-9]+\\.[a-z][a-zA-Z0-9]+$'");
            });
            e.Property(x => x.DocumentType).HasMaxLength(DocumentTypeLength);
            e.Property(x => x.Prefix).HasMaxLength(NumberPattern.MaxAffixLength);
            e.Property(x => x.Suffix).HasMaxLength(NumberPattern.MaxAffixLength);
            e.Property(x => x.Reset).HasMaxLength(10);
            // One sequence per company and document type.
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.DocumentType }).IsUnique();
        });

        modelBuilder.Entity<NumberCounter>(e =>
        {
            e.ToTable("counters", t =>
            {
                t.HasCheckConstraint("ck_counters_next_value", $"next_value BETWEEN 1 AND {NumberPattern.MaxValue}");
                t.HasCheckConstraint("ck_counters_last_issued", "last_issued IS NULL OR last_issued < next_value");
                t.HasCheckConstraint("ck_counters_period", "period ~ '^([0-9]{4}(-[0-9]{2})?)?$'");
            });
            e.Property(x => x.Period).HasMaxLength(PeriodLength);
            // One counter per sequence, branch (or none) and period: the row a number is taken from.
            e.HasIndex(x => new { x.TenantId, x.SequenceId, x.BranchId, x.Period }).IsUnique().AreNullsDistinct(false);
            e.HasOne<NumberSequence>().WithMany().HasForeignKey(x => new { x.TenantId, x.SequenceId })
                .HasPrincipalKey(s => new { s.TenantId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IssuedDocumentNumber>(e =>
        {
            e.ToTable("issued_numbers");
            e.Property(x => x.DocumentType).HasMaxLength(DocumentTypeLength);
            e.Property(x => x.Period).HasMaxLength(PeriodLength);
            e.Property(x => x.Number).HasMaxLength(NumberLength);
            // A number exists once per company and document type, whatever pattern made it.
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.DocumentType, x.Number }).IsUnique();
            // The latest numbers of a sequence (its panel lists them).
            e.HasIndex(x => new { x.TenantId, x.SequenceId, x.CreatedAt });
            e.HasOne<NumberSequence>().WithMany().HasForeignKey(x => new { x.TenantId, x.SequenceId })
                .HasPrincipalKey(s => new { s.TenantId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class NumberingDbContextDesignFactory : IDesignTimeDbContextFactory<NumberingDbContext>
{
    public NumberingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NumberingDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", NumberingDbContext.SchemaName);
        return new NumberingDbContext(options.Options);
    }
}
