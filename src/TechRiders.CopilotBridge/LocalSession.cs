using System.Text.Json;
using ModelContextProtocol.Client;

namespace TechRiders.CopilotBridge;

public sealed record LocalSession(string ApiUrl, string McpUrl, string Bearer, DateTimeOffset ExpiresAt)
{
    public static LocalSession Read(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("Start scripts\\Start-Demos.ps1 before connecting orders.");
        var session = JsonSerializer.Deserialize<LocalSession>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Invalid local session.");
        if (session.ExpiresAt <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(session.Bearer))
            throw new InvalidOperationException("Expired local session. Restart Start-Demos.ps1.");
        ValidateEndpoint(session.ApiUrl);
        ValidateEndpoint(session.McpUrl);
        return session;
    }

    private static void ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            uri.Host != "127.0.0.1" || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Demo credentials may only be sent to loopback HTTP endpoints.");
    }

    public static async Task<McpClient> ConnectAsync(string path, CancellationToken ct = default)
    {
        // Read again on every request: a running Copilot session survives launcher key rotation.
        var session = Read(path);
        return await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = new(session.McpUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {session.Bearer}" }
        }, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), ownsHttpClient: true),
            cancellationToken: ct);
    }
}
