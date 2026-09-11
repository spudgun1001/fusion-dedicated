using System.Text.RegularExpressions;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The server hands the cull and the eviction what is in use. Read from the source
/// because both run against live connections.
/// </summary>
public class InUseGlueTests
{
    /// <summary>Every call to a method, up to the statement's end.</summary>
    private static List<string> Calls(string name)
    {
        string source = FusionServerSource.Text();

        return Regex.Matches(source, Regex.Escape(name) + @"\(")
            .Select(m => source[m.Index..source.IndexOf(");", m.Index, StringComparison.Ordinal)])
            .ToList();
    }

    [Fact]
    public void In_use_is_built_from_hands_holsters_and_loaded_guns()
    {
        string method = FusionServerSource.Method("private HashSet<ushort> EntitiesInUse(");

        Assert.Contains("InUse.Of(", method);
        Assert.Contains("_grabs.All()", method);
        Assert.Contains("_slotted.All()", method);
        Assert.Contains("_loaded", method);
    }

    [Fact]
    public void The_cull_is_told_what_is_in_use()
    {
        var calls = Calls("Entities.CullStaleDetailed");

        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Contains("EntitiesInUse()", call));
    }

    [Fact]
    public void Every_eviction_is_told_what_is_in_use()
    {
        var calls = Calls("Entities.EvictOldest");

        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.Contains("inUse:", call));
    }
}
