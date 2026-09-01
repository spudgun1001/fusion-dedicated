using System.Security.Cryptography;
using System.Text;

namespace FusionDedicated.Web;

public static class DashboardAuth
{
    /// <summary>Compares two secrets in fixed time. A null on either side never matches.</summary>
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

    /// <summary>Splits an HTTP Basic header. Returns null for anything malformed, since this runs on untrusted input.</summary>
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

    /// <summary>An empty configured password disables the check, keeping a localhost panel usable.</summary>
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

    public static bool IsLoopback(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host == "127.0.0.1"
        || host == "::1";

    /// <summary>Why the panel must not bind, or null when it may.</summary>
    public static string? BindRefusalReason(string host, string password)
    {
        if (IsLoopback(host) || !string.IsNullOrEmpty(password))
        {
            return null;
        }

        return $"DashboardHost is '{host}' but no DashboardPassword is set. "
             + "Set a password, or bind the panel to localhost and tunnel in.";
    }
}
