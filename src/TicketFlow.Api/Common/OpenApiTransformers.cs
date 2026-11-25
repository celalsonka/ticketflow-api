using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TicketFlow.Api.Common;

/// <summary>Adds API metadata and the JWT bearer security scheme to the OpenAPI document.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "TicketFlow API",
            Version = "v1",
            Description =
                "Event ticketing REST API: event catalogue, oversell-proof seat reservations, " +
                "idempotent ordering, payments, ticket check-in and sales reporting.\n\n" +
                "**Demo accounts** (password `Passw0rd!`): `admin@ticketflow.dev`, `organizer@ticketflow.dev`, `customer@ticketflow.dev`. " +
                "Call `POST /api/v1/auth/login`, then paste the `accessToken` into the Bearer auth box.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT access token from /api/v1/auth/login",
        };

        return Task.CompletedTask;
    }
}

/// <summary>Marks only the operations that actually require authentication with the bearer requirement.</summary>
internal sealed class AuthorizeOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();

        if (requiresAuth)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(DocumentInfoTransformer.SchemeName, context.Document)] = [],
            });
        }

        return Task.CompletedTask;
    }
}
