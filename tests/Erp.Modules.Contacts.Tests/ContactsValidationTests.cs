namespace Erp.Modules.Contacts.Tests;

/// <summary>The forms names, numbers and addresses are compared and stored in.</summary>
public sealed class ContactsValidationTests
{
    [Theory]
    [InlineData("Falcon Building Materials LLC", "Falcon Building Materials L.L.C.")]
    [InlineData("Falcon Building Materials LLC", "falcon  building materials co.")]
    [InlineData("Gulf Pipe Supplies FZE", "GULF PIPE SUPPLIES")]
    [InlineData("Al-Noor Trading", "Al Noor Trading")]
    [InlineData("فالكون لمواد البناء ذ.م.م", "فالكون لمواد البناء")]
    [InlineData("مؤسسة الواحة للتجارة", "الواحه للتجاره")]
    [InlineData("أحمد الهاشمي", "احمد الهاشمى")]
    public void Names_that_differ_only_in_case_punctuation_legal_form_or_arabic_spelling_compare_equal(string one, string other) =>
        Assert.Equal(ContactsValidation.NameKey(one), ContactsValidation.NameKey(other));

    [Theory]
    [InlineData("Falcon Trading", "Falcon Logistics")]
    [InlineData("Co.", "LLC")]
    public void Different_names_stay_different(string one, string other) =>
        Assert.NotEqual(ContactsValidation.NameKey(one), ContactsValidation.NameKey(other));

    [Theory]
    [InlineData("+971 50 555 0102", "tel:971505550102")]
    [InlineData("00971 50 555 0102", "tel:971505550102")]
    [InlineData("050 555 0102", "tel:971505550102")]
    [InlineData("505550102", "tel:971505550102")]
    [InlineData("04 555 0100", "tel:97145550100")]
    [InlineData("+968 2455 0600", "tel:96824550600")]
    [InlineData("٠٥٠ ٥٥٥ ٠١٠٢", "tel:971505550102")]
    [InlineData("", null)]
    public void A_number_compares_by_its_digits_with_the_UAE_code_whichever_way_it_was_typed(string typed, string? key) =>
        Assert.Equal(key, ContactsValidation.PhoneKey(typed));

    [Theory]
    [InlineData("100-1234-5678-9003", "100123456789003")]
    [InlineData("١٠٠ ١٢٣٤ ٥٦٧٨ ٩٠٠٣", "100123456789003")]
    [InlineData("  ", null)]
    public void A_tax_registration_number_is_stored_as_its_fifteen_digits(string typed, string? stored) =>
        Assert.Equal(stored, ContactsValidation.NormalizeTaxNumber(typed));

    [Theory]
    [InlineData("www.example.ae", "https://www.example.ae")]
    [InlineData("http://example.ae/contact", "http://example.ae/contact")]
    [InlineData("not a site", null)]
    [InlineData("localhost", null)]
    public void A_website_is_kept_with_its_scheme(string typed, string? stored) =>
        Assert.Equal(stored, ContactsValidation.NormalizeWebsite(typed));

    [Theory]
    [InlineData("+971 50 555 0102", true)]
    [InlineData("+971 50 555 010", false)]
    [InlineData("+971 4 555 0100", true)]
    [InlineData("+971 800 1234", true)]
    [InlineData("+44 20 7946 0958", true)]
    public void A_number_with_the_UAE_code_has_the_length_its_kind_has(string phone, bool fits) =>
        Assert.Equal(fits, ContactsValidation.UaeNumberLengthFits(phone));

    [Fact]
    public void Tags_are_kept_once_each_in_the_order_typed()
    {
        Assert.Equal("Customer; VIP", ContactsValidation.JoinTags([" Customer", "VIP", "customer", ""]));
        Assert.Null(ContactsValidation.JoinTags([]));
        Assert.Equal(["Customer", "VIP"], ContactsValidation.TagsOf("Customer; VIP"));
    }

    /// <summary>The API description's phone pattern accepts exactly what the server's two checks
    /// accept (its characters, then the UAE length), so a client that follows the description (the
    /// isolation gate's writes do, with a suffix after the documented example) sends a number the server takes.</summary>
    [Fact]
    public void The_documented_phone_pattern_accepts_exactly_what_the_server_accepts()
    {
        var shape = new System.Text.RegularExpressions.Regex(ContactsValidation.PhoneShapePattern);
        var random = new Random(971);
        const string alphabet = "0123456789 ()-";
        var candidates = new List<string> { "+971 4 123 4567", "+971 50 123 4567", "+971 4 123 4567-1-2", "+971 4 123 4567-1234-5", "+971 4 123 4567-ab", "009715 0555 0199", "+971-50-555-0199", "+971 (4) 555 0100" };
        foreach (var start in new[] { "+971", "00971", "+97", "971", "+44", "0", "+9710", "+9718", "+971800", "+971200" })
        {
            for (var i = 0; i < 400; i++)
            {
                var length = random.Next(0, 16);
                candidates.Add(start + new string(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray()));
            }
        }
        foreach (var phone in candidates)
        {
            var server = shape.IsMatch(phone) && ContactsValidation.UaeNumberLengthFits(phone);
            Assert.True(server == ContactsValidation.DocumentedPhoneRegex().IsMatch(phone), $"'{phone}': server {server}");
        }
    }
}
