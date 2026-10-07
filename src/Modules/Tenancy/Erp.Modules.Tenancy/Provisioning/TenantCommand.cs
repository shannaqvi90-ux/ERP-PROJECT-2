using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Erp.Kernel.Data;
using Erp.Kernel.Http;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Kernel.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Modules.Tenancy.Provisioning;

/// <summary>
/// Platform operator commands (<c>Erp.Host tenant …</c>, or <c>./erp tenant …</c>):
/// <list type="bullet">
/// <item><c>create</c> provisions a customer workspace: the tenant, its first company and head
/// office, the Administrator role and the first administrator, written as the application role
/// bound to the new tenant (row-level security and the audit trail apply).</item>
/// <item><c>suspend</c> and <c>activate</c> stop and restart every session of a workspace.</item>
/// <item><c>list</c> shows every workspace.</item>
/// </list>
/// Finding a workspace by code across tenants needs the operator's database administrator
/// connection (<c>ConnectionStrings:Admin</c>); the web application never has it.
/// Every change names the operator who made it in the audit trail's correlation
/// (<c>operator:&lt;name&gt;:tenant-&lt;action&gt;</c>): <c>--operator &lt;name&gt;</c>, else
/// <c>ERP_OPERATOR</c>, else the signed-in account and machine (<c>user@host</c>).
/// </summary>
internal static partial class TenantCommand
{
    public const string PasswordVariable = "ERP_TENANT_ADMIN_PASSWORD";

    /// <summary>The operator's name when <c>--operator</c> is not given.</summary>
    public const string OperatorVariable = "ERP_OPERATOR";

    public static readonly ModuleCommand Definition = new(
        "tenant",
        "tenant create --code <code> --name-en <name> --name-ar <name> --admin-email <e-mail> --admin-name <name> [--language en|ar] [--operator <name>] | " +
        "tenant suspend --code <code> [--operator <name>] | tenant activate --code <code> [--operator <name>] | tenant list",
        RunAsync);

    private static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var verb = args.Count > 0 ? args[0] : "";
        var options = Options(args.Skip(1).ToList());
        try
        {
            return verb switch
            {
                "create" => await CreateAsync(services, options, cancellationToken),
                "suspend" => await SetStatusAsync(services, options, TenantStatus.Suspended, cancellationToken),
                "activate" => await SetStatusAsync(services, options, TenantStatus.Active, cancellationToken),
                "list" => await ListAsync(services, cancellationToken),
                _ => Usage(),
            };
        }
        catch (CommandException error)
        {
            await Console.Error.WriteLineAsync($"tenant {verb}: {error.Message}");
            return 2;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: " + Definition.Usage);
        return 2;
    }

    private static async Task<int> CreateAsync(IServiceProvider services, IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken)
    {
        var code = Required(options, "code").Trim().ToLowerInvariant();
        var nameEn = Required(options, "name-en").Trim();
        var nameAr = Required(options, "name-ar").Trim();
        var email = Required(options, "admin-email").Trim();
        var adminName = Required(options, "admin-name").Trim();
        var language = options.GetValueOrDefault("language", "en");
        if (!CodeRegex().IsMatch(code)) throw new CommandException("--code: 2 to 40 lower-case letters, digits or hyphens, starting with a letter or digit.");
        if (nameEn.Length > 200 || nameAr.Length > 200) throw new CommandException("--name-en and --name-ar: at most 200 characters.");
        if (!Validator.IsEmail(email)) throw new CommandException("--admin-email: not a valid e-mail address.");
        if (adminName.Length > 200) throw new CommandException("--admin-name: at most 200 characters.");
        if (language is not ("en" or "ar")) throw new CommandException("--language: en or ar.");
        var operatorName = Operator(options);
        // Answered plainly before anything is written (the unique index still decides a race).
        if (await FindAsync(services, code, cancellationToken) is not null)
        {
            throw new CommandException($"a workspace with code '{code}' already exists.");
        }

        var password = Environment.GetEnvironmentVariable(PasswordVariable);
        var generated = string.IsNullOrEmpty(password);
        if (generated)
        {
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(15)).Replace('+', '-').Replace('/', '_');
        }
        if (password!.Length is < PasswordHasher.MinLength or > PasswordHasher.MaxLength)
        {
            throw new CommandException($"{PasswordVariable}: {PasswordHasher.MinLength} to {PasswordHasher.MaxLength} characters.");
        }

        var id = Guid.CreateVersion7();
        var tenant = new SeedTenant(id, code, nameEn, nameAr, email[(email.IndexOf('@') + 1)..], null, 0,
            new SeedAdministrator(email, adminName, language, password));
        try
        {
            await services.GetRequiredService<SeedRunner>().RunAsync(SeedPlan.Provision(tenant), cancellationToken, Correlation(operatorName, "create"));
        }
        catch (Exception error) when (error is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }
                                          or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new CommandException($"a workspace with code '{code}' already exists.");
        }
        Console.WriteLine($"Workspace '{code}' created ({id}).");
        Console.WriteLine($"First administrator: {email}");
        if (generated)
        {
            Console.WriteLine($"Initial password (shown once; ask them to change it): {password}");
        }
        return 0;
    }

    private static async Task<int> SetStatusAsync(IServiceProvider services, IReadOnlyDictionary<string, string> options, string status, CancellationToken cancellationToken)
    {
        var code = Required(options, "code").Trim().ToLowerInvariant();
        var operatorName = Operator(options);
        var id = await FindAsync(services, code, cancellationToken) ?? throw new CommandException($"no workspace with code '{code}'.");
        await using var scope = services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        session.CorrelationId = Correlation(operatorName, status == TenantStatus.Suspended ? "suspend" : "activate");
        await session.BeginAsync(id, null, "system", cancellationToken);
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        tenant.Status = status;
        await db.SaveChangesAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
        Console.WriteLine($"Workspace '{code}' is now {status}.");
        return 0;
    }

    private static async Task<int> ListAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAdminAsync(services, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT t.code, t.status, t.name_en, (SELECT count(*) FROM tenancy.companies c WHERE c.tenant_id = t.id) FROM tenancy.tenants t ORDER BY t.code", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Console.WriteLine("code\tstatus\tcompanies\tname");
        while (await reader.ReadAsync(cancellationToken))
        {
            Console.WriteLine($"{reader.GetString(0)}\t{reader.GetString(1)}\t{reader.GetInt64(3)}\t{reader.GetString(2)}");
        }
        return 0;
    }

    private static async Task<Guid?> FindAsync(IServiceProvider services, string code, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAdminAsync(services, cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id FROM tenancy.tenants WHERE code = @code", connection);
        command.Parameters.AddWithValue("code", code);
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    /// <summary>The database administrator connection on the ERP database (operators only).</summary>
    private static async Task<NpgsqlConnection> OpenAdminAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var admin = configuration.GetConnectionString(ConnectionNames.Admin)
                    ?? throw new CommandException("needs ConnectionStrings:Admin (the database administrator connection); run it through ./erp tenant.");
        var app = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString(ConnectionNames.App));
        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = app.Database };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>Who runs the command: <c>--operator</c>, else <c>ERP_OPERATOR</c>, else
    /// <c>user@host</c>. Letters, digits and <c>. _ @ -</c> only, at most 60 characters.</summary>
    internal static string Operator(IReadOnlyDictionary<string, string> options)
    {
        var name = options.TryGetValue("operator", out var given) ? given.Trim()
            : Environment.GetEnvironmentVariable(OperatorVariable) is { Length: > 0 } variable ? variable.Trim()
            : $"{Environment.UserName}@{Environment.MachineName}";
        if (!OperatorRegex().IsMatch(name))
        {
            throw new CommandException($"--operator (or {OperatorVariable}): 1 to 60 letters, digits or . _ @ - (no spaces).");
        }
        return name;
    }

    /// <summary>The audit trail's correlation for an operator's change (at most 100 characters).</summary>
    private static string Correlation(string operatorName, string action) => $"operator:{operatorName}:tenant-{action}";

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new CommandException($"--{name} is required.");

    /// <summary>--name value pairs.</summary>
    internal static IReadOnlyDictionary<string, string> Options(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new CommandException($"unexpected argument '{args[i]}'.");
            }
            var name = args[i][2..];
            var equals = name.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                options[name[..equals]] = name[(equals + 1)..];
            }
            else if (i + 1 < args.Count)
            {
                options[name] = args[++i];
            }
            else
            {
                throw new CommandException($"--{name} needs a value.");
            }
        }
        return options;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"^[\p{L}\p{Nd}._@-]{1,60}$")]
    private static partial Regex OperatorRegex();

    private sealed class CommandException(string message) : Exception(message);
}
