using FusionDedicated;
using FusionDedicated.Server.Bans;
using FusionDedicated.Server.Ranks;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The stores are read on the relay's main loop while the panel, RCON, stdin and the
/// file watcher all write to them. Without locking these throw
/// InvalidOperationException or corrupt the dictionary.
/// </summary>
public class StoreConcurrencyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-conc-" + Guid.NewGuid());

    public StoreConcurrencyTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const int Iterations = 3000;

    [Fact]
    public void Ranks_survive_concurrent_reads_writes_and_reloads()
    {
        var store = new RankStore(Path.Combine(_dir, "ranks.json"));
        var failures = new List<Exception>();

        Parallel.Invoke(
            () => Repeat(failures, i => store.Set((ulong)(i % 50), $"n{i}", PermissionLevel.Operator)),
            () => Repeat(failures, i => store.Get((ulong)(i % 50))),
            () => Repeat(failures, _ => { foreach (var _e in store.Entries) { } }),
            () => Repeat(failures, i => { if (i % 200 == 0) store.Save(); }),
            () => Repeat(failures, i => { if (i % 300 == 0) store.Load(); }));

        Assert.Empty(failures);
    }

    [Fact]
    public void Bans_survive_concurrent_reads_writes_and_reloads()
    {
        var store = new BanStore(Path.Combine(_dir, "bans.json"));
        var failures = new List<Exception>();

        Parallel.Invoke(
            () => Repeat(failures, i => store.Ban((ulong)(i % 50), $"n{i}", "spam")),
            () => Repeat(failures, i => store.Find((ulong)(i % 50))),
            () => Repeat(failures, i => store.Unban((ulong)(i % 50))),
            () => Repeat(failures, _ => { foreach (var _e in store.Entries) { } }),
            () => Repeat(failures, i => { if (i % 200 == 0) store.Save(); }));

        Assert.Empty(failures);
    }

    private static void Repeat(List<Exception> failures, Action<int> action)
    {
        for (var i = 0; i < Iterations; i++)
        {
            try
            {
                action(i);
            }
            catch (Exception ex)
            {
                lock (failures)
                {
                    failures.Add(ex);
                }

                return;
            }
        }
    }
}
