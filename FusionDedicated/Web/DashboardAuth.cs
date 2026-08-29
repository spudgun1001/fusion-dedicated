using System.Security.Cryptography;
using System.Text;

namespace FusionDedicated.Web;

public static class DashboardAuth
{
    /// <summary>
    /// Compares two secrets without leaking their contents through timing. A null on
    /// either side never matches, including null against null.
    /// </summary>
    public static bool ConstantTimeEquals(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private const string Scheme = "Basic ";

    /// <summary>
    /// Splits an HTTP Basic header into its two halves. Returns null for anything
    /// malformed rather than throwing, because this runs on untrusted input.
    /// </summary>
    public static (string User, string Password)? TryParseBasic(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string encoded = header[Scheme.Length..].Trim();

        if (encoded.Length == 0)
        {
            return null;
        }

        string decoded;

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }

        int split = decoded.IndexOf(':');

        if (split < 0)
        {
            return null;
        }

        return (decoded[..split], decoded[(split + 1)..]);
    }

    /// <summary>
    /// True when the request may proceed. An empty configured password disables the
    /// check entirely, which is what keeps a passwordless localhost panel usable.
    /// </summary>
    public static bool IsAuthorized(string? header, string user, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return true;
        }

        var parsed = TryParseBasic(header);

        if (parsed is null)
        {
            return false;
        }

        return ConstantTimeEquals(parsed.Value.User, user)
            && ConstantTimeEquals(parsed.Value.Password, password);
    }
}
