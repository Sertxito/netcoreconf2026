using TechRiders.Api;
using TechRiders.Infrastructure;
using TechRiders.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddDemoDefaults();
builder.Services.AddBusiness(builder.Configuration);
builder.Services.AddDemoApi();
var app = builder.Build();
app.UseDemoBoundary();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Tech Riders Demo v1"));

app.MapControllers();
app.Run();

public partial class Program;
