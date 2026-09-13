namespace FusionDedicated.Tests.Server;

/// <summary>Reads Program.cs for tests that pin startup wiring, which runs only against live Steam.</summary>
internal static class ProgramSource
{
    public static string Text()
        => File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FusionDedicated", "Program.cs"))
            .Replace("\r\n", "\n");

    /// <summary>The text from one marker up to the next, for pinning one block.</summary>
    public static string Between(string from, string to)
    {
        string source = Text();
        int start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"'{from}' is not in Program.cs");

        int end = source.IndexOf(to, start, StringComparison.Ordinal);

        Assert.True(end > start, $"'{to}' does not follow '{from}' in Program.cs");

        return source[start..end];
    }
}
