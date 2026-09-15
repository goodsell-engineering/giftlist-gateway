using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gateway.IntegrationTests.Support;

/// <summary>
/// A minimal GraphQL-over-HTTP client — enters through the real
/// <c>/graphql</c> endpoint HotChocolate maps (CONVENTIONS.md "Testing": entered at its real entry
/// point), never by calling a resolver or interactor directly. Deliberately thin (no generated
/// client, unlike <c>AuthService.AuthServiceClient</c> for grpc-web): GL-23 has exactly two
/// queries, so a hand-rolled POST is less machinery than wiring up codegen for them.
/// </summary>
internal static class GraphQlClient
{
    public static async Task<GraphQlResponse> QueryAsync(
        HttpClient client, string query, object? variables = null, string? accessToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = JsonContent.Create(new { query, variables }),
        };

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>()
            ?? throw new InvalidOperationException("The GraphQL endpoint returned an empty body.");

        var root = body.RootElement;
        var data = root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind != JsonValueKind.Null
            ? dataElement
            : (JsonElement?)null;

        var errors = new List<GraphQlError>();
        if (root.TryGetProperty("errors", out var errorsElement))
        {
            foreach (var error in errorsElement.EnumerateArray())
            {
                var message = error.GetProperty("message").GetString() ?? string.Empty;
                string? code = null;
                string? errorCode = null;
                if (error.TryGetProperty("extensions", out var extensions))
                {
                    code = extensions.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
                    errorCode = extensions.TryGetProperty("errorCode", out var errorCodeElement)
                        ? errorCodeElement.GetString()
                        : null;
                }

                errors.Add(new GraphQlError(message, code, errorCode));
            }
        }

        return new GraphQlResponse(data, errors);
    }
}

/// <summary><paramref name="Data"/> is null when every field of the query failed.</summary>
internal sealed record GraphQlResponse(JsonElement? Data, IReadOnlyList<GraphQlError> Errors);

/// <summary>
/// <paramref name="Code"/> is HotChocolate's own <c>extensions.code</c>
/// (<c>BuildingBlocks.Transport.ErrorKindTransportMapping.ToGraphQlCode</c>, e.g. <c>FORBIDDEN</c>).
/// <paramref name="ErrorCode"/> is the stable <c>Error.Code</c>
/// (<c>ErrorToGraphQlErrorMapper.ErrorCodeExtensionKey</c>, e.g. <c>gateway.forbidden</c>) —
/// CONVENTIONS.md "Errors"'s two-segment machine code, the one a client actually branches on.
/// </summary>
internal sealed record GraphQlError(string Message, string? Code, string? ErrorCode);
