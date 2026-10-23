using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechRiders.Application;

namespace TechRiders.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        var key = configuration["DEMO_AUTH_KEY"] ?? throw new InvalidOperationException("Use Start-Demos.ps1: missing ephemeral DEMO_AUTH_KEY.");
        var path = configuration["DEMO_DB_PATH"] ?? throw new InvalidOperationException("Use launcher: missing explicit DEMO_DB_PATH.");
        services.AddSingleton(new DemoSecurity(key));
        services.AddSingleton<IReviewSigner>(sp => sp.GetRequiredService<DemoSecurity>());
        services.AddSingleton(new SqliteOrderStore(path));
        services.AddSingleton<IOrderStore>(sp => sp.GetRequiredService<SqliteOrderStore>());
        // Handlers are stateless; each operation owns its SQLite connection and transaction.
        services.AddSingleton<GetPendingOrdersHandler>();
        services.AddSingleton<GetOrderReviewContextHandler>();
        services.AddSingleton<CancelOrderHandler>();
        services.AddSingleton<OrderCrudHandler>();
        services.AddAuthentication(DemoAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(DemoAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();
        return services;
    }
}
