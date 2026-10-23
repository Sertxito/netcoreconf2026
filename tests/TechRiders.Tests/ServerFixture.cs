using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using TechRiders.AgentDemo;
using TechRiders.Infrastructure;

namespace TechRiders.Tests;

public sealed class ServerFixture : IAsyncLifetime
{
    private readonly List<Process> processes = [];
    private readonly ConcurrentQueue<string> logs = new();
    public string Root { get; }
    public string DataDirectory { get; }
    public SqliteOrderStore Store { get; }
    public DemoSecurity Security { get; }
    public DemoRunner Runner { get; }
    public string AgentUrl { get; }
    private readonly string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private readonly int apiPort = FreePort();
    private readonly int mcpPort = FreePort();
    private readonly int agentPort = FreePort();
    public string ApiUrl => $"http://127.0.0.1:{apiPort}";
    public string[] Logs => logs.ToArray();

    public ServerFixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TechRiders.slnx"))) root = root.Parent;
        Root = root?.FullName ?? throw new InvalidOperationException("Solution root not found.");
        DataDirectory = Path.Combine(Root, ".local", "tests", Guid.NewGuid().ToString("N"));
        Store = new(Path.Combine(DataDirectory, "test.db"));
        Security = new(secret);
        AgentUrl = $"http://127.0.0.1:{agentPort}";
        Runner = new(Security, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DEMO_API_URL"] = ApiUrl, ["DEMO_MCP_URL"] = $"http://127.0.0.1:{mcpPort}/mcp"
        }).Build());
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async Task InitializeAsync()
    {
        try
        {
            Start("Api", apiPort);
            Start("Mcp", mcpPort);
            Start("AgentDemo", agentPort);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            foreach (var port in new[] { apiPort, mcpPort, agentPort })
            {
                var ready = false;
                for (var attempt = 0; attempt < 150; attempt++)
                {
                    if (processes.Any(p => p.HasExited)) throw new InvalidOperationException(string.Join("\n", logs));
                    try
                    {
                        using var response = await http.GetAsync($"http://127.0.0.1:{port}/health");
                        if (response.IsSuccessStatusCode) { ready = true; break; }
                    }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) { }
                    await Task.Delay(100);
                }
                if (!ready) throw new InvalidOperationException($"Server timeout: {string.Join("\n", logs)}");
            }
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private void Start(string name, int port)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.Combine(Root, "src", $"TechRiders.{name}", "bin", configuration, "net10.0", $"TechRiders.{name}.dll");
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        info.ArgumentList.Add(dll);
        info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        info.Environment["DEMO_AUTH_KEY"] = secret;
        info.Environment["DEMO_DB_PATH"] = Path.Combine(DataDirectory, "test.db");
        info.Environment["DEMO_API_URL"] = ApiUrl;
        info.Environment["DEMO_MCP_URL"] = $"http://127.0.0.1:{mcpPort}/mcp";
        info.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
        var process = Process.Start(info)!;
        processes.Add(process);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public Task DisposeAsync()
    {
        foreach (var process in processes)
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(10000); }
            process.Dispose();
        }
        processes.Clear();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
        return Task.CompletedTask;
    }
}
