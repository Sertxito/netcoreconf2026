using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TechRiders.Application;

namespace TechRiders.Infrastructure;

// Local demonstration issuer only: the random launch key is never sent by clients or stored in source.
public sealed class DemoSecurity : IReviewSigner
{
    private readonly byte[] key;
    public DemoSecurity(string secret)
    {
        if (secret.Length < 32) throw new InvalidOperationException("DEMO_AUTH_KEY must be a fresh random secret of at least 32 characters.");
        key = Encoding.UTF8.GetBytes(secret);
    }

    private string Protect<T>(T value)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value));
        var signature = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
        return $"{payload}.{signature}";
    }

    private T Unprotect<T>(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new BusinessException("INVALID_SIGNED_CONTEXT", "Signed demo context is missing or invalid.", 409);
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(
                HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(parts[0])), Convert.FromBase64String(parts[1])))
                throw new FormatException();
            return JsonSerializer.Deserialize<T>(Convert.FromBase64String(parts[0])) ?? throw new FormatException();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw new BusinessException("INVALID_SIGNED_CONTEXT", "Signed demo context is missing or invalid.", 409);
        }
    }

    private sealed record Identity(string Audience, string Subject, string[] Permissions, long Expires);

    public string IssueToken(bool canCancel = true) => Protect(new Identity("techriders-local-demo", "local-demo-presenter",
        canCancel ? ["orders.read", "orders.write", "orders.cancel"] : ["orders.read"],
        DateTimeOffset.UtcNow.AddHours(4).ToUnixTimeSeconds()));

    public ClaimsPrincipal Authenticate(string token)
    {
        var identity = Unprotect<Identity>(token);
        if (identity.Audience != "techriders-local-demo" || identity.Expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
            identity.Subject != "local-demo-presenter")
            throw new BusinessException("UNAUTHENTICATED", "Invalid or expired demo bearer.", 401);
        return new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, identity.Subject), new Claim(ClaimTypes.Name, "Local Demo (trusted launcher)") }
                .Concat(identity.Permissions.Select(p => new Claim("permission", p))), "DemoSignedBearer"));
    }

    public string Sign(ReviewProof proof) => Protect(proof);
    public ReviewProof Verify(string token) => Unprotect<ReviewProof>(token);
}
