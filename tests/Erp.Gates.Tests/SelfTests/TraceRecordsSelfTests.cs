using System.Reflection;
using Erp.Gates.Tests.Infrastructure;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>Each kind of record the statement trace keeps in its files comes back with every field it went in with.</summary>
public sealed class TraceRecordsSelfTests
{
    private static dynamic Log(string name) =>
        typeof(SqlTrace).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void Calls_binds_settings_and_changes_round_trip()
    {
        var app = new object();
        var tag = Guid.NewGuid().ToString("N");

        var call = new TracedCall("identity.resolve_session", "auth.session", "/api/auth/" + tag, true, app, 5432, "select identity.resolve_session(@t) " + tag, "erp_" + tag);
        var callNulls = new TracedCall("identity.verify_sign_in", null, null, false, null, null, "stmt " + tag, null);
        dynamic calls = Log("Calls");
        calls.Add(call);
        calls.Add(callNulls);
        var backCalls = ((IEnumerable<TracedCall>)calls.From(0)).Where(c => c.Statement.EndsWith(tag, StringComparison.Ordinal) || c.Path == "/api/auth/" + tag).ToList();
        Assert.Equal([call, callNulls], backCalls.OrderBy(c => c.Function == "identity.verify_sign_in").ToList(), new RecordComparer<TracedCall>());

        var bind = new TracedBind("tenant-" + tag, "kernel", "ep", "/api/x/" + tag, "GET", false, "tenant-" + tag, app) { Request = "req-" + tag, Sequence = 1234567890123, ReadOnlyRequest = true };
        dynamic binds = Log("Binds");
        binds.Add(bind);
        var backBind = ((IEnumerable<TracedBind>)binds.From(0)).Single(b => b.Request == "req-" + tag);
        Assert.Equal(bind, backBind);
        Assert.Same(app, backBind.App);

        var setting = new TracedSetting("select set_config('app.tenant_id', @p, true) " + tag, null, "/p/" + tag, "Erp.Kernel.Data.ErpDbSession", true, "erp_app", app);
        dynamic settings = Log("Settings");
        settings.Add(setting);
        Assert.Equal(setting, ((IEnumerable<TracedSetting>)settings.From(0)).Single(s => s.Path == "/p/" + tag));

        foreach (var local in new bool?[] { true, false, null })
        {
            var change = new TracedSettingChange(new SqlSettings.Change("app.tenant_id", tag, local == false, local, "set_config " + tag), "ep", "/c/" + tag + local, "POST", "req-" + tag, "tenant", true, "Caller", 4242, app);
            dynamic changes = Log("Changes");
            changes.Add(change);
            var back = ((IEnumerable<TracedSettingChange>)changes.From(0)).Single(c => c.Path == "/c/" + tag + local);
            Assert.Equal(change.Change, back.Change);
            Assert.Equal((change.Endpoint, change.Method, change.Request, change.RequiredTenant, change.ResolvingSession, change.Caller, change.ProcessId),
                (back.Endpoint, back.Method, back.Request, back.RequiredTenant, back.ResolvingSession, back.Caller, back.ProcessId));
            Assert.Same(app, back.App);
        }
        var nameless = new TracedSettingChange(new SqlSettings.Change(null, null, true, null, "computed " + tag), null, "/n/" + tag, null, null, null, false, null, 1, null);
        ((dynamic)Log("Changes")).Add(nameless);
        Assert.Equal(nameless.Change, ((IEnumerable<TracedSettingChange>)((dynamic)Log("Changes")).From(0)).Single(c => c.Path == "/n/" + tag).Change);

        dynamic readOnlys = Log("ReadOnlys");
        readOnlys.Add(("req-ro-" + tag, 77L, app));
        Assert.Single(((IEnumerable<(string Request, long Sequence, object? App)>)readOnlys.From(0)).ToList(), r => r.Request == "req-ro-" + tag && r.Sequence == 77 && ReferenceEquals(r.App, app));
    }

    private sealed class RecordComparer<T> : IEqualityComparer<T>
    {
        public bool Equals(T? x, T? y) => EqualityComparer<T>.Default.Equals(x, y);

        public int GetHashCode(T obj) => EqualityComparer<T>.Default.GetHashCode(obj!);
    }
}
