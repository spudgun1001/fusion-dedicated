using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Web;

public sealed class PanelAccount
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "viewer";

    [JsonPropertyName("salt")]
    public string Salt { get; set; } = "";

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("iterations")]
    public int Iterations { get; set; } = PanelUsers.DefaultIterations;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";
}

/// <summary>
/// Who may open the panel and what they may do in it. Passwords are stored as a
/// PBKDF2 hash with a salt of their own, so the file is safe to read and a
/// shared password does not show as a shared hash.
/// </summary>
public sealed class PanelUsers
{
    public const int DefaultIterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly object _lock = new();

    private Dictionary<string, PanelAccount> _accounts =
        new(StringComparer.OrdinalIgnoreCase);

    // A bot polling with Basic auth would otherwise pay for PBKDF2 on every
    // request. Keyed on the stored hash too, so a new password drops the entry.
    private static readonly TimeSpan RememberFor = TimeSpan.FromMinutes(5);
    private const int RememberCap = 64;

    private readonly Dictionary<string, (string StoredHash, byte[] PasswordSha, DateTime At)> _verified =
        new(StringComparer.OrdinalIgnoreCase);

    public PanelUsers(string path) => _path = path;

    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>How many times Authenticate ran the slow hash. Tests read it.</summary>
    internal int Derivations;

    public IReadOnlyDictionary<string, PanelAccount> Accounts
    {
        get { lock (_lock) { return new Dictionary<string, PanelAccount>(_accounts, StringComparer.OrdinalIgnoreCase); } }
    }

    public int Count
    {
        get { lock (_lock) { return _accounts.Count; } }
    }

    /// <summary>Adds an account, or changes the password and role of one that exists.</summary>
    public void Set(string name, string password, PanelRole role)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);

        var account = new PanelAccount
        {
            Role = role.ToString().ToLowerInvariant(),
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(Derive(password, salt, DefaultIterations)),
            Iterations = DefaultIterations,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
        };

        lock (_lock)
        {
            if (_accounts.TryGetValue(name, out var existing))
            {
                account.CreatedAt = existing.CreatedAt;
            }

            _accounts[name] = account;
        }
    }

    public bool Remove(string name)
    {
        lock (_lock)
        {
            return _accounts.Remove(name);
        }
    }

    /// <summary>The account's role, or null when the name or password is wrong.</summary>
    public PanelRole? Authenticate(string? name, string? password)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        PanelAccount? account;

        lock (_lock)
        {
            if (!_accounts.TryGetValue(name, out account))
            {
                return null;
            }
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(account.Salt);
            expected = Convert.FromBase64String(account.Hash);
        }
        catch (FormatException)
        {
            return null;
        }

        byte[] passwordSha = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        DateTime now = Clock();

        lock (_lock)
        {
            if (_verified.TryGetValue(name, out var seen)
                && seen.StoredHash == account.Hash
                && now - seen.At < RememberFor
                && CryptographicOperations.FixedTimeEquals(passwordSha, seen.PasswordSha))
            {
                return PanelPermissions.ParseRole(account.Role);
            }
        }

        Interlocked.Increment(ref Derivations);
        byte[] actual = Derive(password, salt, account.Iterations);

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return null;
        }

        lock (_lock)
        {
            if (_verified.Count >= RememberCap && !_verified.ContainsKey(name))
            {
                _verified.Remove(_verified.MinBy(e => e.Value.At).Key);
            }

            _verified[name] = (account.Hash, passwordSha, now);
        }

        return PanelPermissions.ParseRole(account.Role);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            Math.Max(1, iterations), HashAlgorithmName.SHA256, HashBytes);
    }

    /// <summary>Reads the file, keeping what is loaded if it will not parse.</summary>
    public void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, PanelAccount>>(
                File.ReadAllText(_path), Options);

            if (parsed is null)
            {
                return;
            }

            lock (_lock)
            {
                _accounts = new Dictionary<string, PanelAccount>(parsed, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception e) when (e is JsonException or IOException
            or UnauthorizedAccessException)
        {
            // A broken file must not lock everyone out of a running server, and
            // a file being written at this moment throws from the read rather
            // than the parse.
        }
    }

    public void Save()
    {
        try
        {
            Dictionary<string, PanelAccount> forDisk;

            lock (_lock)
            {
                forDisk = new Dictionary<string, PanelAccount>(_accounts, StringComparer.OrdinalIgnoreCase);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            File.WriteAllText(_path, JsonSerializer.Serialize(forDisk, Options));
        }
        catch
        {
            // The list stays correct in memory until a write succeeds.
        }
    }
}
