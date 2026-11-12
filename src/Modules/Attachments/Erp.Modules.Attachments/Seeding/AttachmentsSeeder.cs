using System.Text;
using Erp.Kernel.Modules;
using Erp.Kernel.Seeding;
using Erp.Modules.Attachments.Contracts;
using Erp.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Attachments.Seeding;

/// <summary>
/// Gives the demo and the gate workspaces a few files on their first contacts, so the screens and the
/// isolation gates have files to show and to try to reach. The bytes go through the store like any
/// upload; tenant B's carry its canary (in their names and their text), so a file that leaks is
/// recognised. Runs after the contacts seeder and finds the contacts through the contacts module's
/// registration, never its tables. A minimal or newly provisioned workspace has none.
/// </summary>
internal sealed class AttachmentsSeeder(AttachmentsDbContext db, ModuleCatalog catalog, IAttachmentStore store, IUserDirectory users) : ITenantSeeder
{
    public const string ContactsTable = "contacts.contacts";

    public int Order => 150;

    /// <summary>A 1 by 1 pixel PNG (the demo's picture).</summary>
    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        var record = catalog.AttachmentRecordTypes.FirstOrDefault(r => r.Table == ContactsTable);
        if (context.Plan.Profile is SeedProfile.Minimal or SeedProfile.Provision || record?.FirstIds is null || await db.Files.AnyAsync(cancellationToken))
        {
            return;
        }
        var demo = context.Plan.Profile == SeedProfile.Demo;
        var contacts = await record.FirstIds(context.Services, demo ? 5 : 3, cancellationToken);
        var at = new DateTimeOffset(2024, 2, 1, 9, 0, 0, TimeSpan.Zero);
        // The demo's people added them: the administrator the licence and the picture, the accountant the delivery note
        // (found through the identity module's public contract; a workspace without them leaves the adder empty).
        var domain = context.Tenant.EmailDomain;
        var administrator = (await users.FindByEmailAsync($"admin@{domain}", cancellationToken))?.Id;
        var accountant = (await users.FindByEmailAsync($"accountant@{domain}", cancellationToken))?.Id ?? administrator;
        var adders = new List<Guid?>();
        var number = 0;
        foreach (var contact in contacts)
        {
            number++;
            var licence = $"TL-2026-{70000 + number}";
            var files = new List<(string Name, string ContentType, byte[] Bytes, Guid? By)>
            {
                (context.Mark($"Trade licence {licence}") + ".pdf", "application/pdf", Pdf(context.Mark($"Trade licence {licence}")), administrator),
                (context.Mark("Delivery note") + ".txt", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(context.Mark("Delivery note: 12 pallets, signed on receipt.") + "\n"), accountant),
            };
            if (demo)
            {
                files.Add(("company-seal-" + number + ".png", "image/png", Convert.FromBase64String(PngBase64), administrator));
            }
            foreach (var (name, contentType, bytes, by) in files)
            {
                using var stream = new MemoryStream(bytes);
                var blob = await store.SaveAsync(context.Tenant.Id, stream, AttachmentLimits.MaxFileBytes, cancellationToken);
                db.Files.Add(new AttachedFile
                {
                    RecordType = ContactsTable,
                    RecordId = contact,
                    Name = name.Length > AttachmentLimits.MaxNameLength ? name[..(AttachmentLimits.MaxNameLength - 4)] + name[^4..] : name,
                    ContentType = contentType,
                    SizeBytes = blob.Size,
                    Sha256 = blob.Sha256,
                    StorageKey = blob.Key,
                });
                adders.Add(by);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        // The context stamps new rows with the seeding run (no person, now): the demo's files carry who added them and when.
        var index = 0;
        foreach (var file in db.Files.Local.ToList())
        {
            var by = adders[index++];
            var stamp = at.AddMinutes(index * 7);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attachments.files SET created_by = {by}::uuid, updated_by = {by}::uuid, created_at = {stamp}, updated_at = {stamp} WHERE id = {file.Id}", cancellationToken);
        }
    }

    /// <summary>A one-page PDF holding a line of text (ASCII only: the text of an Arabic or accented name is left out).</summary>
    internal static byte[] Pdf(string text)
    {
        var line = new string(text.Select(c => c is >= ' ' and <= '~' && c is not ('(' or ')' or '\\') ? c : ' ').ToArray());
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 120] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            null,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var content = $"BT /F1 12 Tf 20 60 Td ({line}) Tj ET";
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(builder.Length);
            builder.Append(i + 1).Append(" 0 obj\n");
            builder.Append(objects[i] ?? $"<< /Length {content.Length} >>\nstream\n{content}\nendstream").Append("\nendobj\n");
        }
        var xref = builder.Length;
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }
        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
