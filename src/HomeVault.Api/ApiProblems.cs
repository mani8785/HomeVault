namespace HomeVault.Api;

internal static class ApiProblems
{
    internal static IResult Result(int status, string? code = null, string? field = null)
    {
        code ??= status switch
        {
            400 => "invalid_request",
            401 => "unauthenticated",
            403 => "forbidden",
            404 => "unavailable",
            409 => "conflict",
            413 => "request_too_large",
            415 => "unsupported_media_type",
            429 => "rate_limited",
            _ => "request_failed"
        };
        var errors = new Dictionary<string, string[]>();
        if (field is not null) errors[field] = [code];
        return Results.Problem(type: "about:blank", title: "Request failed.", statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code, ["errors"] = errors });
    }
}
