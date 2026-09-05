using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>
/// The panel was served with no cache headers, so a browser held an old copy
/// after every release and the fix was to tell people to hard refresh.
/// </summary>
public class PageCacheTests
{
    [Fact]
    public void Same_content_gives_the_same_tag()
    {
        Assert.Equal(PageCache.ETagFor("<html>a</html>"), PageCache.ETagFor("<html>a</html>"));
    }

    [Fact]
    public void Different_content_gives_a_different_tag()
    {
        Assert.NotEqual(PageCache.ETagFor("<html>a</html>"), PageCache.ETagFor("<html>b</html>"));
    }

    [Fact]
    public void A_tag_is_quoted_so_it_is_a_valid_header()
    {
        var tag = PageCache.ETagFor("anything");

        Assert.StartsWith("\"", tag);
        Assert.EndsWith("\"", tag);
    }

    [Fact]
    public void A_matching_tag_means_the_browser_may_keep_what_it_has()
    {
        var tag = PageCache.ETagFor("<html>a</html>");

        Assert.True(PageCache.IsFresh(tag, tag));
    }

    [Fact]
    public void A_stale_tag_means_it_must_take_the_new_copy()
    {
        var was = PageCache.ETagFor("<html>a</html>");
        var now = PageCache.ETagFor("<html>b</html>");

        Assert.False(PageCache.IsFresh(was, now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_tag_from_the_browser_means_send_the_page(string? sent)
    {
        Assert.False(PageCache.IsFresh(sent, PageCache.ETagFor("<html>a</html>")));
    }

    [Fact]
    public void A_browser_may_send_several_tags()
    {
        var tag = PageCache.ETagFor("<html>a</html>");

        Assert.True(PageCache.IsFresh("\"old\", " + tag, tag));
    }

    [Fact]
    public void A_weak_tag_still_matches_its_own_content()
    {
        var tag = PageCache.ETagFor("<html>a</html>");

        Assert.True(PageCache.IsFresh("W/" + tag, tag));
    }
}
