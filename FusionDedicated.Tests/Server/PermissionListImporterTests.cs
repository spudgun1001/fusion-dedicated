using FusionDedicated;
using FusionDedicated.Server.Ranks;

namespace FusionDedicated.Tests.Server;

public class PermissionListImporterTests
{
    private const string Xml = """
    <PermissionList>
      <Permission id="76561198000000000" username="spudgun" level="2" />
      <Permission id="76561198000000001" username="mate" level="1" />
    </PermissionList>
    """;

    private static RankStore Empty()
        => new(Path.Combine(Path.GetTempPath(), "fd-import-" + Guid.NewGuid(), "ranks.json"));

    [Fact]
    public void Imports_every_entry()
    {
        var store = Empty();

        int added = PermissionListImporter.Import(store, Xml);

        Assert.Equal(2, added);
        Assert.Equal(PermissionLevel.Owner, store.Get(76561198000000000));
        Assert.Equal(PermissionLevel.Operator, store.Get(76561198000000001));
        Assert.Equal("spudgun", store.Entries[76561198000000000].Name);
    }

    [Fact]
    public void Import_never_lowers_an_existing_rank()
    {
        var store = Empty();
        store.Set(76561198000000001, "mate", PermissionLevel.Owner);

        PermissionListImporter.Import(store, Xml);

        Assert.Equal(PermissionLevel.Owner, store.Get(76561198000000001));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<PermissionList>")]
    public void Malformed_xml_imports_nothing_rather_than_throwing(string xml)
    {
        Assert.Equal(0, PermissionListImporter.Import(Empty(), xml));
    }

    [Fact]
    public void Entries_with_bad_attributes_are_skipped()
    {
        const string bad = """
        <PermissionList>
          <Permission id="not-a-number" username="x" level="2" />
          <Permission username="no id" level="1" />
          <Permission id="5" username="ok" level="1" />
        </PermissionList>
        """;

        var store = Empty();

        Assert.Equal(1, PermissionListImporter.Import(store, bad));
        Assert.Equal(PermissionLevel.Operator, store.Get(5));
    }

    [Fact]
    public void Out_of_range_levels_are_clamped()
    {
        const string wild = """<PermissionList><Permission id="7" username="x" level="99" /></PermissionList>""";

        var store = Empty();
        PermissionListImporter.Import(store, wild);

        Assert.Equal(PermissionLevel.Owner, store.Get(7));
    }
}
