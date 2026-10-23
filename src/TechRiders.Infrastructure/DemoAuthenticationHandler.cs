using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechRiders.Application;

namespace TechRiders.Infrastructure;

public sealed class DemoAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, DemoSecurity security)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DemoSignedBearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());
        try
        {
            return Task.FromResult(AuthenticateResult.Success(new(security.Authenticate(header[7..]), Scheme.Name)));
        }
        catch (BusinessException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid demo bearer."));
        }
    }
}
