namespace Kofre.Server.Api;

/// <summary>The body of a refused request: a stable code clients can test.</summary>
public sealed record ErrorBody(string Error);

/// <summary>The body of a refusal by a store's fence: the fence to name.</summary>
public sealed record FenceRefusal(string Error, long? Fence);

/// <summary>The refusals the API returns.</summary>
internal static class Errors
{
    public static IResult BadRequest(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status400BadRequest);

    public static IResult Unauthorized(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status401Unauthorized);

    public static IResult Forbidden(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status403Forbidden);

    public static IResult NotFound(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status404NotFound);

    public static IResult Conflict(string code) => Results.Json(new ErrorBody(code), statusCode: StatusCodes.Status409Conflict);

    /// <summary>An upload below the store's fence, or a fence lowered: the current fence returned.</summary>
    public static IResult Fenced(string code, long? fence) => Results.Json(new FenceRefusal(code, fence), statusCode: StatusCodes.Status409Conflict);

    public static IResult TooLarge() => Results.Json(new ErrorBody("too-large"), statusCode: StatusCodes.Status413PayloadTooLarge);
}
