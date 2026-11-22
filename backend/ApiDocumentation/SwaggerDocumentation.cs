using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace Chubb.ApiDocumentation;

internal static class SwaggerDocumentation
{
    private const string Bearer = "Bearer";

    public static void AddAssessmentOpenApi(this IServiceCollection services, string title)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = title;
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[Bearer] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Paste the accessToken returned by POST /auth/login on the Claims API."
                };
                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.Any(item => item is IAllowAnonymous) ||
                    !metadata.Any(item => item is IAuthorizeData or AuthorizationPolicy))
                {
                    return Task.CompletedTask;
                }

                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(Bearer, context.Document)] = []
                });
                operation.Responses ??= new OpenApiResponses();
                operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized" });
                operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden" });
                return Task.CompletedTask;
            });
        });
    }

    public static void UseAssessmentSwagger(this WebApplication app, string title)
    {
        if (!app.Environment.IsDevelopment())
            return;

        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.DocumentTitle = title;
            options.SwaggerEndpoint("../openapi/v1.json", title);
        });
    }
}
