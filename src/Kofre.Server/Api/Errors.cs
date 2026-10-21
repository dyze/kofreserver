namespace Kofre.Server.Api;

/// <summary>The body of a refused request: a stable code clients can test.</summary>
public sealed record ErrorBody(string Error);

/// <summary>The refusals the API returns.</summary>
internal static class Errors
{
    public static IResult BadRequest(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status400BadRequest);

    public static IResult Unauthorized(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status401Unauthorized);

    public static IResult Forbidden(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status403Forbidden);

    public static IResult NotFound(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status404NotFound);

    public static IResult Conflict(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status409Conflict);

    public static IResult TooLarge() => Results.Json(new ErrorBody("too-large"), statusCode: StatusCodes.Status413PayloadTooLarge);
}
