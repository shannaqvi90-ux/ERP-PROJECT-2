namespace Erp.Kernel.Http;

/// <summary>
/// The only values a text field of a request accepts. The OpenAPI document publishes them as the
/// field's <c>enum</c>, so API clients (and the gate attacks that build valid bodies from the
/// document) know them. It documents; the endpoint still validates the value itself.
/// On a positional record parameter, target the property: <c>[property: AllowedTextValues("en", "ar")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class AllowedTextValuesAttribute(params string[] values) : Attribute
{
    public IReadOnlyList<string> Values { get; } = values;
}
