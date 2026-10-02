using Erp.Host;
using Erp.Kernel.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddErpPlatform(ErpModules.For(builder.Environment, builder.Configuration));

var app = builder.Build();
app.UseErpPlatform();

// `Erp.Host migrate`, `Erp.Host seed demo`: run the command and exit instead of serving.
if (await app.TryRunCommandAsync(args))
{
    return;
}

await app.RunAsync();

/// <summary>Entry point; public so integration tests can host it.</summary>
public partial class Program;
