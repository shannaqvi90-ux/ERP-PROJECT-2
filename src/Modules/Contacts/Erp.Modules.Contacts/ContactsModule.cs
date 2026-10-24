using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Shell;
using Erp.Modules.Contacts.Contracts;
using Erp.Modules.Contacts.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Contacts;

/// <summary>
/// The shared contacts directory: organisations (customers, suppliers, authorities) and people,
/// who may belong to an organisation. Master data every later module (sales, purchase, HR,
/// accounting) refers to through <see cref="IContactDirectory"/>; it holds no business transactions.
/// A contact belongs to the whole workspace, not to one company: every company of it deals with the
/// same customer (docs/decisions/p16-contacts-workspace-directory.md). Contacts are archived, never
/// deleted, so every document that names one keeps its name.
/// </summary>
public sealed class ContactsModule : ErpModule
{
    public override string Name => "contacts";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. ContactsPermissions.All]);
        module.DbContext<ContactsDbContext>();
        module.Services.AddScoped<IContactDirectory, ContactDirectory>();
        module.Endpoints(ContactEndpoints.Map);
        module.Menu(new MenuEntry("contacts.contacts", "contacts.menu.contacts", "/contacts/contacts", ContactsPermissions.ContactsRead, Order: 100));
        module.List(ContactsList.Create());
        // Reports print and export the list exactly as the screen shows it, and a contact's card.
        module.ListRows(ContactsList.Key, async (services, request, http, cancellationToken) =>
            (await ContactEndpoints.PageAsync(services.GetRequiredService<ContactsDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.Report<Reports.ContactCardReport>(Reports.ContactCardReport.Definition);
        module.Seeder<ContactsSeeder>();
    }
}

/// <summary>
/// One contact: an organisation or a person. Names in English and Arabic (at least one); e-mail,
/// phone and mobile numbers in international form; a UAE-style address (emirate, city, area, PO
/// box) or one abroad; website and tags; the trade licence and tax registration numbers as plain
/// fields; the currency it trades in and an optional credit limit kept as money (amount, currency,
/// the exchange rate used and the amount in the base currency, CLAUDE.md rule 2).
/// </summary>
public sealed class Contact : TenantEntity
{
    /// <summary><see cref="ContactKinds.Organisation"/> or <see cref="ContactKinds.Person"/>.</summary>
    public string Kind { get; set; } = ContactKinds.Organisation;

    /// <summary>For a person: the organisation they work for (a contact of the same workspace, of
    /// kind organisation). Organisations belong to no other contact.</summary>
    public Guid? OrganisationId { get; set; }

    /// <summary>The contact's reference (C000123), unique in the workspace when given. Typed for
    /// now; document numbering (p10) will offer the next one from a sequence.</summary>
    public string? Reference { get; set; }

    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";

    /// <summary>A person's position at their organisation (Purchasing Manager).</summary>
    public string? JobTitle { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Website { get; set; }

    /// <summary>Street, building and office (one line each).</summary>
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }

    /// <summary>The area or district within the city (Al Quoz, Mussafah).</summary>
    public string? Area { get; set; }
    public string? City { get; set; }

    /// <summary>One of the seven emirates, stored in camel case (abuDhabi), for an address in the UAE.</summary>
    public string? Emirate { get; set; }
    public string? PoBox { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string Country { get; set; } = ContactsValidation.UnitedArabEmirates;

    /// <summary>The address as written in Arabic, for Arabic documents.</summary>
    public string? AddressAr { get; set; }

    /// <summary>Words the workspace sorts contacts by (Customer, Supplier, VIP), stored as one text
    /// joined by "; " in the order typed, each at most once.</summary>
    public string? Tags { get; set; }

    public string? TradeLicenceNumber { get; set; }

    /// <summary>The Tax Registration Number: 15 digits, stored without spaces or dashes and with
    /// Arabic-Indic digits turned into 0 to 9 (docs/compliance/vat-trn-format.md, owner-signed).</summary>
    public string? TaxRegistrationNumber { get; set; }

    /// <summary>ISO 4217 code of the currency the contact is invoiced in or pays in.</summary>
    public string DefaultCurrency { get; set; } = "AED";

    /// <summary>The credit limit: the amount in its currency, the exchange rate used and the amount
    /// in the base currency of the company it was set in. All six columns are null when no limit is
    /// set (none is not the same as zero, which means no credit).</summary>
    public decimal? CreditLimitAmount { get; set; }
    public string? CreditLimitCurrency { get; set; }
    public decimal? CreditLimitExchangeRate { get; set; }
    public decimal? CreditLimitBaseAmount { get; set; }
    public string? CreditLimitBaseCurrency { get; set; }
    public bool? CreditLimitStated { get; set; }

    public string? Notes { get; set; }

    /// <summary>False once archived: the contact is kept (documents still name it) but no longer
    /// offered for new work; it is restored by the same permission.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The name compared for duplicates: lower case, letters and digits only, single spaces.</summary>
    public string NameKey { get; set; } = "";

    /// <summary>The Arabic name compared for duplicates, folded the same way.</summary>
    public string NameArKey { get; set; } = "";

    /// <summary>The e-mail compared for duplicates: lower case, trimmed.</summary>
    public string? EmailKey { get; set; }

    /// <summary>The phone and mobile numbers compared for duplicates: digits only, a UAE number as
    /// 971 and its national number whichever way it was typed.</summary>
    public string? PhoneKey { get; set; }
    public string? MobileKey { get; set; }

    /// <summary>The credit limit as one money value, or null when none is set.</summary>
    public Erp.Kernel.Money.Money? CreditLimit
    {
        get => CreditLimitAmount is { } amount && CreditLimitCurrency is { } currency && CreditLimitExchangeRate is { } rate && CreditLimitBaseCurrency is { } baseCurrency
            ? Erp.Kernel.Money.Money.Unrounded(amount, new Erp.Kernel.Money.CurrencyCode(currency), rate, new Erp.Kernel.Money.CurrencyCode(baseCurrency))
            : null;
        set
        {
            CreditLimitAmount = value?.Amount;
            CreditLimitCurrency = value?.Currency;
            CreditLimitExchangeRate = value?.ExchangeRate;
            CreditLimitBaseAmount = value?.BaseAmount;
            CreditLimitBaseCurrency = value?.BaseCurrency;
            CreditLimitStated = value?.IsStated;
        }
    }
}

public sealed class ContactsDbContext : ModuleDbContext
{
    public ContactsDbContext(DbContextOptions<ContactsDbContext> options, ITenantContext? tenant = null)
        : base(options, tenant)
    {
    }

    public const string SchemaName = "contacts";

    protected override string Schema => SchemaName;

    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Contact>(e =>
        {
            e.ToTable("contacts", t =>
            {
                t.HasCheckConstraint("ck_contacts_kind", "kind IN ('organisation', 'person')");
                t.HasCheckConstraint("ck_contacts_name", "btrim(name_en) <> '' OR btrim(name_ar) <> ''");
                // Organisations belong to no other contact, and nothing belongs to itself.
                t.HasCheckConstraint("ck_contacts_organisation", "organisation_id IS NULL OR (kind = 'person' AND organisation_id <> id)");
                t.HasCheckConstraint("ck_contacts_country", "country ~ '^[A-Z]{2}$'");
                t.HasCheckConstraint("ck_contacts_emirate", "emirate IS NULL OR (country = 'AE' AND emirate IN ('abuDhabi', 'dubai', 'sharjah', 'ajman', 'ummAlQuwain', 'rasAlKhaimah', 'fujairah'))");
                t.HasCheckConstraint("ck_contacts_default_currency", "default_currency ~ '^[A-Z]{3}$'");
                // docs/compliance/vat-trn-format.md: exactly 15 digits, as stored.
                t.HasCheckConstraint("ck_contacts_tax_registration_number", "tax_registration_number ~ '^[0-9]{15}$'");
                // CLAUDE.md rule 2: an amount never without its currency, rate and base amount.
                t.HasCheckConstraint("ck_contacts_credit_limit",
                    "(credit_limit_amount IS NULL AND credit_limit_currency IS NULL AND credit_limit_exchange_rate IS NULL AND credit_limit_base_amount IS NULL AND credit_limit_base_currency IS NULL AND credit_limit_stated IS NULL) OR " +
                    "(credit_limit_amount >= 0 AND credit_limit_currency ~ '^[A-Z]{3}$' AND credit_limit_exchange_rate > 0 AND credit_limit_base_amount >= 0 " +
                    "AND credit_limit_base_currency ~ '^[A-Z]{3}$' AND credit_limit_stated IS NOT NULL AND (credit_limit_currency <> credit_limit_base_currency OR credit_limit_exchange_rate = 1))");
            });
            e.Property(x => x.Kind).HasMaxLength(12);
            e.Property(x => x.Reference).HasMaxLength(30);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.JobTitle).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Mobile).HasMaxLength(30);
            e.Property(x => x.Website).HasMaxLength(200);
            e.Property(x => x.AddressLine1).HasMaxLength(200);
            e.Property(x => x.AddressLine2).HasMaxLength(200);
            e.Property(x => x.Area).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Emirate).HasMaxLength(20);
            e.Property(x => x.PoBox).HasMaxLength(20);
            e.Property(x => x.Country).HasMaxLength(2);
            e.Property(x => x.AddressAr).HasMaxLength(400);
            e.Property(x => x.Tags).HasMaxLength(400);
            e.Property(x => x.TradeLicenceNumber).HasMaxLength(50);
            e.Property(x => x.TaxRegistrationNumber).HasMaxLength(15);
            e.Property(x => x.DefaultCurrency).HasMaxLength(3).IsFixedLength().HasColumnType("char(3)");
            e.Property(x => x.CreditLimitAmount).HasPrecision(Erp.Kernel.Money.Money.AmountPrecision, Erp.Kernel.Money.Money.AmountScale);
            e.Property(x => x.CreditLimitCurrency).HasMaxLength(3).IsFixedLength().HasColumnType("char(3)");
            e.Property(x => x.CreditLimitExchangeRate).HasPrecision(Erp.Kernel.Money.Money.RatePrecision, Erp.Kernel.Money.Money.RateScale);
            e.Property(x => x.CreditLimitBaseAmount).HasPrecision(Erp.Kernel.Money.Money.BaseAmountPrecision, Erp.Kernel.Money.Money.BaseAmountScale);
            e.Property(x => x.CreditLimitBaseCurrency).HasMaxLength(3).IsFixedLength().HasColumnType("char(3)");
            e.Ignore(x => x.CreditLimit);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.NameKey).HasMaxLength(200);
            e.Property(x => x.NameArKey).HasMaxLength(200);
            e.Property(x => x.EmailKey).HasMaxLength(254);
            e.Property(x => x.PhoneKey).HasMaxLength(30);
            e.Property(x => x.MobileKey).HasMaxLength(30);
            e.Property(x => x.IsActive).HasDefaultValue(true);

            // The reference is unique in the workspace when given.
            e.HasIndex(x => new { x.TenantId, x.Reference }).IsUnique().HasFilter("reference IS NOT NULL");
            // A person's organisation is a contact of the same workspace (the database refuses
            // another workspace's id: the key is (tenant_id, id)); an organisation with people is
            // archived, never deleted.
            e.HasOne<Contact>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganisationId })
                .HasPrincipalKey(c => new { c.TenantId, c.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.OrganisationId });
            // Duplicate warning: equal name, e-mail, number or tax registration number.
            e.HasIndex(x => new { x.TenantId, x.NameKey });
            e.HasIndex(x => new { x.TenantId, x.NameArKey });
            e.HasIndex(x => new { x.TenantId, x.EmailKey });
            e.HasIndex(x => new { x.TenantId, x.PhoneKey });
            e.HasIndex(x => new { x.TenantId, x.MobileKey });
            e.HasIndex(x => new { x.TenantId, x.TaxRegistrationNumber });
            // List framework: word search on trigram indexes, keyset order on (tenant, column, id).
            e.HasIndex(x => new { x.NameEn, x.NameAr, x.Email, x.Reference, x.Phone, x.Mobile, x.TaxRegistrationNumber }, "ix_contacts_search")
                .HasMethod("gin").HasOperators("gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops");
            e.HasIndex(x => new { x.TenantId, x.NameEn, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Reference, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Email, x.Id });
            e.HasIndex(x => new { x.TenantId, x.City, x.Id });
            e.HasIndex(x => new { x.TenantId, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.TenantId, x.UpdatedAt, x.Id });
        });
    }
}

internal sealed class ContactsDbContextDesignFactory : IDesignTimeDbContextFactory<ContactsDbContext>
{
    public ContactsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ContactsDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", ContactsDbContext.SchemaName);
        return new ContactsDbContext(options.Options);
    }
}

/// <summary>Contacts as other modules see them (<see cref="IContactDirectory"/>).</summary>
internal sealed class ContactDirectory(ContactsDbContext db) : IContactDirectory
{
    public async Task<ContactInfo?> GetAsync(Guid contactId, CancellationToken cancellationToken) =>
        await db.Contacts.AsNoTracking().Where(c => c.Id == contactId)
            .Select(c => new ContactInfo(c.Id, c.Kind, c.Reference, c.NameEn, c.NameAr, c.OrganisationId, c.Email, c.Phone, c.Mobile,
                c.TaxRegistrationNumber, c.DefaultCurrency, c.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
}
