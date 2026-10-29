using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Numbering.Contracts;

/// <summary>Permissions of the numbering module.</summary>
public static class NumberingPermissions
{
    /// <summary>See how each company numbers each kind of document, and the next number of each.</summary>
    public const string SequencesRead = "numbering.sequences.read";

    /// <summary>Change how a company numbers a kind of document (its pattern, when it starts again,
    /// whether it may skip numbers, the next number).</summary>
    public const string SequencesUpdate = "numbering.sequences.update";

    public static readonly IReadOnlyList<string> All = [SequencesRead, SequencesUpdate];
}

/// <summary>When a sequence starts again from 1.</summary>
public static class NumberReset
{
    /// <summary>Never: the numbers keep growing.</summary>
    public const string Never = "never";

    /// <summary>On 1 January: each calendar year of the document date has its own numbers.</summary>
    public const string Yearly = "yearly";

    /// <summary>On the first of each month: each month of the document date has its own numbers.</summary>
    public const string Monthly = "monthly";

    public static readonly IReadOnlyList<string> All = [Never, Yearly, Monthly];
}

/// <summary>
/// A kind of document that is numbered (a purchase order, a tax invoice). A module registers the
/// kinds it numbers with <see cref="NumberingRegistration.AddNumberedDocumentType"/>; the numbering
/// module gives every company a sequence for each, starting from these defaults, which the company
/// then changes on the Document numbering screen.
/// </summary>
/// <param name="Key">Unique, <c>module.kind</c> in lower camel case (for example <c>purchasing.order</c>).</param>
/// <param name="LabelKey">Server string key of the kind's name, in English and Arabic (the registering
/// module's <c>Resources/en.json</c> and <c>ar.json</c>), for example <c>numbering.documentType.purchasing.order</c>.</param>
/// <param name="Prefix">Default text before the number; may hold the tokens of <see cref="NumberPattern"/>.</param>
/// <param name="Padding">Default number of digits (leading zeros), 1 to <see cref="NumberPattern.MaxPadding"/>.</param>
/// <param name="Reset">Default <see cref="NumberReset"/>.</param>
/// <param name="Gapless">Default for whether the numbers may never skip (see <see cref="IDocumentNumbers"/>).</param>
/// <param name="Suffix">Default text after the number.</param>
public sealed record NumberedDocumentType(string Key, string LabelKey, string Prefix, int Padding, string Reset, bool Gapless, string Suffix = "")
{
    /// <summary>Problems with the definition itself (checked when the module starts).</summary>
    public IEnumerable<string> Problems()
    {
        if (!NumberPattern.IsDocumentTypeKey(Key))
        {
            yield return $"numbered document type '{Key}': the key must look like 'module.kind' (lower camel case)";
        }
        foreach (var problem in NumberPattern.Check(Prefix, Suffix, Padding, Reset, perBranch: false))
        {
            yield return $"numbered document type '{Key}': its default pattern is not valid ({problem.Field}: {problem.Code})";
        }
    }
}

/// <summary>Registration of numbered document types.</summary>
public static class NumberingRegistration
{
    /// <summary>Number documents of this kind: every company gets a sequence for it (from the
    /// defaults), shown on the Document numbering screen, and <see cref="IDocumentNumbers"/> numbers them.</summary>
    public static IServiceCollection AddNumberedDocumentType(this IServiceCollection services, NumberedDocumentType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return services.AddSingleton(type);
    }
}

/// <summary>What a module asks a number for.</summary>
/// <param name="DocumentType">A registered <see cref="NumberedDocumentType.Key"/>.</param>
/// <param name="CompanyId">The company the document belongs to (in the caller's company scope).</param>
/// <param name="DocumentDate">The document's date in the workspace's time zone: it decides the year
/// and month of the number and, for a sequence that starts again each year or month, its period.</param>
/// <param name="BranchId">The branch the document belongs to; required when the company numbers this
/// kind per branch.</param>
/// <param name="SourceId">The document the number is for, kept with the issued number so every number
/// traces back to its document.</param>
public sealed record NumberRequest(string DocumentType, Guid CompanyId, DateOnly DocumentDate, Guid? BranchId = null, Guid? SourceId = null);

/// <summary>A number taken for a document.</summary>
/// <param name="Number">The document number as printed (for example <c>PO-2026-00148</c>).</param>
/// <param name="Value">The counter's value inside it (148).</param>
/// <param name="Period">The period it was counted in: "" (never starts again), "2026" or "2026-10".</param>
/// <param name="Gapless">Taken from a gapless sequence (inside the caller's transaction).</param>
public sealed record IssuedNumber(string Number, long Value, string Period, bool Gapless, Guid SequenceId, Guid? BranchId);

/// <summary>The number the next document would get, without taking it.</summary>
public sealed record NumberPreview(string Number, long Value, string Period, bool Gapless, Guid SequenceId, Guid? BranchId);

/// <summary>
/// Document numbers for later modules. Call <see cref="TakeAsync"/> inside the unit of work that
/// saves the document. A <b>gapless</b> sequence gives the number inside that same transaction: the
/// next document of the same sequence waits until this one is saved or abandoned, and an abandoned
/// document gives its number back, so the numbers that exist run on with no gap. Any other sequence
/// gives the number in a short transaction of its own, committed at once: documents never wait for
/// each other, and a document whose save fails leaves its number unused (a gap). Either way a number
/// is never given twice for the same company and document type (each issued number is recorded with
/// its document and refused if a changed pattern would repeat one).
/// </summary>
public interface IDocumentNumbers
{
    /// <summary>Takes the next number. Throws <see cref="NumberingException"/> when it cannot.</summary>
    Task<IssuedNumber> TakeAsync(NumberRequest request, CancellationToken cancellationToken);

    /// <summary>The number the next document would get now, without taking it.</summary>
    Task<NumberPreview> PreviewAsync(NumberRequest request, CancellationToken cancellationToken);
}

/// <summary>Why a number could not be given. <see cref="Code"/> is a server string key under
/// <c>problem.</c> (English and Arabic), formatted with <see cref="Arguments"/>.</summary>
public sealed class NumberingException(string code, params object?[] arguments)
    : InvalidOperationException($"Document numbering refused: {code}")
{
    public string Code { get; } = code;

    public IReadOnlyList<object?> Arguments { get; } = arguments;

    /// <summary>No such registered document type.</summary>
    public const string DocumentTypeUnknown = "numbering.documentTypeUnknown";

    /// <summary>The company is not in the caller's company scope, or the caller may not write there.</summary>
    public const string CompanyNotAllowed = "numbering.companyNotAllowed";

    /// <summary>The company numbers this kind per branch and no branch was given.</summary>
    public const string BranchRequired = "numbering.branchRequired";

    /// <summary>The branch given is not one of the company's (or not one the caller may see).</summary>
    public const string BranchNotOfCompany = "numbering.branchNotOfCompany";

    /// <summary>The company has no sequence for the document type (made when the company was made;
    /// missing only if it was never seeded).</summary>
    public const string SequenceMissing = "numbering.sequenceMissing";

    /// <summary>Another document of the same gapless sequence kept its number too long.</summary>
    public const string Busy = "numbering.busy";

    /// <summary>The sequence's pattern now makes a number that was already issued.</summary>
    public const string NumberTaken = "numbering.numberTaken";

    /// <summary>The unit of work only reads (a GET request): no number can be taken in it.</summary>
    public const string ReadOnly = "numbering.readOnly";

    /// <summary>The document date is outside the years a number may carry.</summary>
    public const string DateOutOfRange = "numbering.dateOutOfRange";
}

/// <summary>One problem with a pattern: the field it is about and a validation code (server and web
/// string key <c>validation.{code}</c>).</summary>
public sealed record PatternProblem(string Field, string Code, params object?[] Arguments);

/// <summary>
/// The text of document numbers: a prefix, the counter with leading zeros to a fixed number of
/// digits, and a suffix. Prefix and suffix are letters A to Z, digits and the separators
/// <c>- / . _ #</c>, and may hold the tokens <c>{YYYY}</c> (the document's year), <c>{YY}</c> (its last
/// two digits), <c>{MM}</c> (its month, two digits) and <c>{BRANCH}</c> (the branch's code). The web
/// screen previews numbers with the same rules (<c>web/src/modules/numbering/pattern.ts</c>, kept equal
/// by the shared examples in <c>tests/Erp.Modules.Numbering.Tests/pattern-examples.json</c>).
/// </summary>
public static partial class NumberPattern
{
    public const int MaxAffixLength = 30;
    public const int MinPadding = 1;
    public const int MaxPadding = 12;

    /// <summary>The highest value a counter may reach (fits PostgreSQL's bigint and 18 digits).</summary>
    public const long MaxValue = 999_999_999_999_999_999;

    /// <summary>Earliest and latest document years a number may carry.</summary>
    public const int MinYear = 1990;

    public const int MaxYear = 2199;

    public const string Year = "{YYYY}";
    public const string ShortYear = "{YY}";
    public const string Month = "{MM}";
    public const string Branch = "{BRANCH}";

    public static readonly IReadOnlyList<string> Tokens = [Year, ShortYear, Month, Branch];

    public static bool IsDocumentTypeKey(string key) => DocumentTypeKeyRegex().IsMatch(key);

    /// <summary>The period a document date is counted in: "" (never starts again), "yyyy" or "yyyy-MM".</summary>
    public static string PeriodOf(string reset, DateOnly date) => reset switch
    {
        NumberReset.Yearly => date.Year.ToString("0000", CultureInfo.InvariantCulture),
        NumberReset.Monthly => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => "",
    };

    /// <summary>The period a document date is counted in as stored: its year (0: never starts again)
    /// and month (0: a whole year).</summary>
    public static (int Year, int Month) PeriodPartsOf(string reset, DateOnly date) => reset switch
    {
        NumberReset.Yearly => (date.Year, 0),
        NumberReset.Monthly => (date.Year, date.Month),
        _ => (0, 0),
    };

    /// <summary>A stored period as text: "", "yyyy" or "yyyy-MM".</summary>
    public static string PeriodText(int year, int month) =>
        year == 0 ? "" : month == 0 ? year.ToString("0000", CultureInfo.InvariantCulture) : $"{year.ToString("0000", CultureInfo.InvariantCulture)}-{month.ToString("00", CultureInfo.InvariantCulture)}";

    /// <summary>The number as printed: tokens replaced, the value with leading zeros to
    /// <paramref name="padding"/> digits (a larger value keeps all its digits).</summary>
    public static string Format(string prefix, string suffix, int padding, DateOnly date, string? branchCode, long value) =>
        Expand(prefix, date, branchCode) + value.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Clamp(padding, MinPadding, MaxPadding), '0') + Expand(suffix, date, branchCode);

    private static string Expand(string text, DateOnly date, string? branchCode)
    {
        if (text.IndexOf('{') < 0)
        {
            return text;
        }
        var builder = new StringBuilder(text.Length + 8);
        foreach (var part in Parts(text))
        {
            builder.Append(part switch
            {
                Year => date.Year.ToString("0000", CultureInfo.InvariantCulture),
                ShortYear => (date.Year % 100).ToString("00", CultureInfo.InvariantCulture),
                Month => date.Month.ToString("00", CultureInfo.InvariantCulture),
                Branch => branchCode ?? "",
                _ => part,
            });
        }
        return builder.ToString();
    }

    /// <summary>The text cut into literal runs and whole tokens.</summary>
    private static IEnumerable<string> Parts(string text)
    {
        var at = 0;
        foreach (Match match in TokenRegex().Matches(text))
        {
            if (match.Index > at)
            {
                yield return text[at..match.Index];
            }
            yield return match.Value;
            at = match.Index + match.Length;
        }
        if (at < text.Length)
        {
            yield return text[at..];
        }
    }

    /// <summary>Every problem with a pattern, by field (prefix, suffix, padding, reset, perBranch).</summary>
    public static IReadOnlyList<PatternProblem> Check(string? prefix, string? suffix, int? padding, string? reset, bool perBranch)
    {
        var problems = new List<PatternProblem>();
        CheckAffix(problems, "prefix", prefix ?? "");
        CheckAffix(problems, "suffix", suffix ?? "");
        if (padding is not { } digits || digits < MinPadding || digits > MaxPadding)
        {
            problems.Add(new PatternProblem("padding", "numberingPadding", MinPadding, MaxPadding));
        }
        if (reset is null || !NumberReset.All.Contains(reset))
        {
            problems.Add(new PatternProblem("reset", "numberingReset"));
        }
        var text = (prefix ?? "") + (suffix ?? "");
        var hasYear = text.Contains(Year, StringComparison.Ordinal) || text.Contains(ShortYear, StringComparison.Ordinal);
        if (reset == NumberReset.Yearly && !hasYear)
        {
            problems.Add(new PatternProblem("reset", "numberingYearlyNeedsYear"));
        }
        if (reset == NumberReset.Monthly && (!hasYear || !text.Contains(Month, StringComparison.Ordinal)))
        {
            problems.Add(new PatternProblem("reset", "numberingMonthlyNeedsMonth"));
        }
        if (perBranch && !text.Contains(Branch, StringComparison.Ordinal))
        {
            problems.Add(new PatternProblem("perBranch", "numberingPerBranchNeedsBranch"));
        }
        if (!perBranch && text.Contains(Branch, StringComparison.Ordinal))
        {
            problems.Add(new PatternProblem("perBranch", "numberingBranchNeedsPerBranch"));
        }
        return problems;
    }

    private static void CheckAffix(List<PatternProblem> problems, string field, string text)
    {
        if (text.Length > MaxAffixLength)
        {
            problems.Add(new PatternProblem(field, "maxLength", MaxAffixLength));
            return;
        }
        foreach (var part in Parts(text))
        {
            if (part.StartsWith('{') && Tokens.Contains(part))
            {
                continue;
            }
            if (part.Contains('{') || part.Contains('}'))
            {
                problems.Add(new PatternProblem(field, "numberingToken"));
                return;
            }
            if (!LiteralRegex().IsMatch(part))
            {
                problems.Add(new PatternProblem(field, "numberingCharacters"));
                return;
            }
        }
    }

    [GeneratedRegex(@"\{(?:YYYY|YY|MM|BRANCH)\}")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"^[A-Za-z0-9\-/._#]*$")]
    private static partial Regex LiteralRegex();

    [GeneratedRegex(@"^[a-z][a-zA-Z0-9]{1,30}\.[a-z][a-zA-Z0-9]{1,40}$")]
    private static partial Regex DocumentTypeKeyRegex();
}
