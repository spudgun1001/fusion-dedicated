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
}
