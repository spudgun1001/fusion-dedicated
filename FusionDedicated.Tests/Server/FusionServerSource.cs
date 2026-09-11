namespace FusionDedicated.Tests.Server;

/// <summary>
/// Reads FusionServer.cs for tests that pin glue with no seam to test through,
/// since the message loop needs live Steam connections.
/// </summary>
internal static class FusionServerSource
{
    public static string Text()
        => File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FusionDedicated", "Server", "FusionServer.cs"))
            .Replace("\r\n", "\n");

    /// <summary>One member, from its signature to the brace that closes it.</summary>
    public static string Method(string signature)
    {
        string source = Text();
        int start = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(start >= 0, $"'{signature}' is not in FusionServer.cs");

        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);

        Assert.True(end > start, $"the end of '{signature}' was not found");

        return source[start..end];
    }
}
