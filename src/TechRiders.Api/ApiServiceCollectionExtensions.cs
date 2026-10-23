using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TechRiders.Api;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddDemoApi(this IServiceCollection services)
    {
        services.AddControllers(options =>
        {
            // Keep business validation (and its order/error codes) in the shared REST/MCP handlers.
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            options.ModelBinderProviders.Insert(0, new QueryStringBinder());
            foreach (var formatter in options.InputFormatters.OfType<SystemTextJsonInputFormatter>())
                formatter.SupportedMediaTypes.Remove("text/json");
            options.OutputFormatters.RemoveType<StringOutputFormatter>();
            foreach (var formatter in options.OutputFormatters.OfType<SystemTextJsonOutputFormatter>())
            {
                formatter.SupportedMediaTypes.Remove("text/json");
            }
        }).ConfigureApiBehaviorOptions(options => options.SuppressMapClientErrors = true);
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.OperationFilter<JsonRequestBodyOperationFilter>();
            options.SwaggerDoc("v1", new()
            {
                Title = "Tech Riders — Demo REST", Version = "v1",
                Description = "Local Demo only. Exact status casing and customerId=cus-contoso. CRUD DTO intentionally omits duplicate evidence."
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer",
                Description = "Short-lived local demo token printed by Start-Demos.ps1. Not a production identity provider."
            });
            options.AddSecurityRequirement(document => new()
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        });
        return services;
    }

    private sealed class JsonRequestBodyOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // Retain the original JSON-only OpenAPI contract; the formatter still accepts application/*+json.
            if (operation.RequestBody?.Content is not { } content) return;
            foreach (var mediaType in content.Keys.Where(key => key != "application/json").ToArray())
                content.Remove(mediaType);
        }
    }

    private sealed class QueryStringBinder : IModelBinderProvider, IModelBinder
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
            context.Metadata.ModelType == typeof(string) && context.BindingInfo.BindingSource == BindingSource.Query
                ? this : null;

        public Task BindModelAsync(ModelBindingContext context)
        {
            // Match minimal API strings: preserve empty/whitespace and join repeated query values.
            var value = context.ValueProvider.GetValue(context.ModelName);
            if (value != ValueProviderResult.None)
            {
                context.ModelState.SetModelValue(context.ModelName, value);
                context.Result = ModelBindingResult.Success(value.ToString());
            }
            return Task.CompletedTask;
        }
    }
}
