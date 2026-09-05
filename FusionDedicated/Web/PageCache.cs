using System.Security.Cryptography;
using System.Text;

namespace FusionDedicated.Web;

/// <summary>
/// Lets a browser keep the panel it already has until the file actually
/// changes. Paired with no-cache, which asks it to check every time rather than
/// trust an age: the check is cheap and a stale moderation panel is not.
/// </summary>
public static class PageCache
{
    public static string ETagFor(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));

        return '"' + Convert.ToHexString(hash)[..16].ToLowerInvariant() + '"';
    }

    /// <summary>
    /// Whether what the browser holds is still current. A browser may send
    /// several tags, and may mark one weak, so match on any of them.
    /// </summary>
    public static bool IsFresh(string? ifNoneMatch, string current)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        foreach (string candidate in ifNoneMatch.Split(','))
        {
            string tag = candidate.Trim();

            if (tag.StartsWith("W/", StringComparison.Ordinal))
            {
                tag = tag[2..];
            }

            if (tag == current)
            {
                return true;
            }
        }

        return false;
    }
}
