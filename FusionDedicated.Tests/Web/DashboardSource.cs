namespace FusionDedicated.Tests.Web;

/// <summary>Reads Dashboard.cs for tests that pin glue the panel has no seam for.</summary>
internal static class DashboardSource
{
    public static string Text()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "FusionDedicated", "Web", "Dashboard.cs");

            if (File.Exists(path))
            {
                return File.ReadAllText(path).Replace("\r\n", "\n");
            }
        }

        throw new FileNotFoundException("Dashboard.cs");
    }

    /// <summary>One member, from its signature to the brace that closes it.</summary>
    public static string Method(string signature)
    {
        string source = Text();
        int start = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(start >= 0, $"'{signature}' is not in Dashboard.cs");

        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);

        Assert.True(end > start, $"the end of '{signature}' was not found");

        return source[start..end];
    }
}
