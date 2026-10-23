using TechRiders.Infrastructure;
using TechRiders.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddDemoDefaults();
builder.Services.AddBusiness(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();
var app = builder.Build();
app.UseDemoBoundary();
app.UseAuthentication();
app.UseAuthorization();
app.MapMcp("/mcp").RequireAuthorization();
app.Run();

public partial class Program;
