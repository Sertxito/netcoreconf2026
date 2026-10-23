using System.Security.Cryptography;
using System.Text.Json;
using TechRiders.Domain;

namespace TechRiders.Application;

internal static class ReviewEvidence
{
    public static string Hash(Order order, Order original) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { order, original })));
}
