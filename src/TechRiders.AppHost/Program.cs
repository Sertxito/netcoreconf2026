using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);
var key = builder.Configuration["DEMO_AUTH_KEY"] ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
var db = builder.Configuration["DEMO_DB_PATH"] ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".local", "techriders.db"));
var api = builder.AddProject<Projects.TechRiders_Api>("api", launchProfileName: null)
    .WithHttpEndpoint(port: 5101, targetPort: 5101, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:5101")
    .WithEnvironment("DEMO_AUTH_KEY", key).WithEnvironment("DEMO_DB_PATH", db)
    .WithHttpHealthCheck("/health");
var mcp = builder.AddProject<Projects.TechRiders_Mcp>("mcp", launchProfileName: null)
    .WithHttpEndpoint(port: 5102, targetPort: 5102, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:5102")
    .WithEnvironment("DEMO_AUTH_KEY", key).WithEnvironment("DEMO_DB_PATH", db)
    .WithHttpHealthCheck("/health");
if (bool.TryParse(builder.Configuration["DEMO_INCLUDE_AGENT_DEMO"], out var includeAgentDemo) && includeAgentDemo)
{
    builder.AddProject<Projects.TechRiders_AgentDemo>("agent-demo", launchProfileName: null)
    .WithHttpEndpoint(port: 5103, targetPort: 5103, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:5103")
    .WithEnvironment("DEMO_AUTH_KEY", key).WithEnvironment("DEMO_DB_PATH", db)
    .WithEnvironment("DEMO_API_URL", "http://127.0.0.1:5101")
    .WithEnvironment("DEMO_MCP_URL", "http://127.0.0.1:5102/mcp")
        .WithHttpHealthCheck("/health").WaitFor(api).WaitFor(mcp);
}
builder.Build().Run();
