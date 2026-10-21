using Kofre.Server.Stores;
using Microsoft.AspNetCore.Http.Features;

namespace Kofre.Server.Api;

public sealed record CreatedStore(Guid Id);

public sealed record AddedFile(string Path);

/// <summary>Stores, their files and feed. Access is checked at every request against the store's members.</summary>
internal static class StoreEndpoints
{
    public const int MaxFeedPage = 1000;

    public static void Map(RouteGroupBuilder api)
    {
        var stores = api.MapGroup("/stores").RequireAuthorization().AddEndpointFilter(Caller.RequireActive);
        stores.MapGet("/", List);
        stores.MapPost("/", Create);
        stores.MapGet("/{id:guid}/stats", Stats);
        stores.MapDelete("/{id:guid}", Delete);
        stores.MapPost("/{id:guid}/restore", Restore);
        stores.MapGet("/{id:guid}/feed", Feed);
        stores.MapGet("/{id:guid}/files/{**path}", Download);
        stores.MapPut("/{id:guid}/files/{**path}", Upload);
    }

    private static IResult List(HttpContext context, StoreRepository stores) => Results.Ok(stores.ListFor(Caller.Of(context).Id));

    private static IResult Create(HttpContext context, StoreRepository stores, ILogger<Store> log)
    {
        var store = stores.Create(Caller.Of(context).Id);
        log.LogInformation("Store {Store} created by {Account}", store.Id, Caller.Of(context).Id);
        return Results.Created($"/v1/stores/{store.Id}", new CreatedStore(store.Id));
    }

    private static IResult Stats(Guid id, HttpContext context, StoreRepository stores)
    {
        var store = stores.Find(id);
        return Refuse(context, store, Role.Reader) ?? Results.Ok(store!.Stats());
    }

    private static IResult Delete(Guid id, HttpContext context, StoreRepository stores, TimeProvider clock, ILogger<Store> log)
    {
        var store = stores.Find(id);
        if (Refuse(context, store, Role.Owner) is { } refused)
        {
            return refused;
        }

        if (!Caller.RepeatsPassword(context))
        {
            return Errors.Forbidden("wrong-password");
        }

        store!.SetDeleted(clock.GetUtcNow());
        log.LogInformation("Store {Store} moved to the trash by {Account}", id, Caller.Of(context).Id);
        return Results.NoContent();
    }

    private static IResult Restore(Guid id, HttpContext context, StoreRepository stores, ILogger<Store> log)
    {
        if (stores.Find(id) is not { } store)
        {
            return Errors.NotFound("no-store");
        }

        if (store.RoleOf(Caller.Of(context).Id) != Role.Owner)
        {
            return Errors.Forbidden("not-allowed");
        }

        if (!Caller.RepeatsPassword(context))
        {
            return Errors.Forbidden("wrong-password");
        }

        if (store.Info.Deleted is null)
        {
            return Errors.Conflict("not-deleted");
        }

        store.SetDeleted(null);
        log.LogInformation("Store {Store} restored from the trash by {Account}", id, Caller.Of(context).Id);
        return Results.NoContent();
    }

    private static IResult Feed(Guid id, long? since, int? limit, HttpContext context, StoreRepository stores)
    {
        var store = stores.Find(id);
        return Refuse(context, store, Role.Reader)
            ?? Results.Ok(store!.FeedSince(since ?? 0, Math.Clamp(limit ?? MaxFeedPage, 1, MaxFeedPage)));
    }

    private static IResult Download(Guid id, string path, HttpContext context, StoreRepository stores, ILogger<Store> log)
    {
        var store = stores.Find(id);
        if (Refuse(context, store, Role.Reader) is { } refused)
        {
            return refused;
        }

        if (!StorePath.IsValid(path) || store!.FileOf(path) is not { } file)
        {
            return Errors.NotFound("no-file");
        }

        log.LogDebug("Store {Store}: {Path} downloaded", id, path);
        return Results.File(Path.GetFullPath(file), "application/octet-stream");
    }

    private static async Task<IResult> Upload(Guid id, string path, HttpContext context, StoreRepository stores, ILogger<Store> log)
    {
        var store = stores.Find(id);
        if (Refuse(context, store, Role.Writer) is { } refused)
        {
            return refused;
        }

        if (!StorePath.IsValid(path))
        {
            return Errors.BadRequest("invalid-path");
        }

        var max = Caller.Options(context).MaxUploadBytes;
        if (context.Request.ContentLength > max)
        {
            return Errors.TooLarge();
        }

        // The limit is checked while copying: Kestrel's own (30 MB by default) is lifted for uploads.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = null;
        }

        switch (await store!.AddAsync(path, context.Request.Body, max, context.RequestAborted))
        {
            case AddResult.Added:
                log.LogDebug("Store {Store}: {Path} uploaded", id, path);
                return Results.Created($"/v1/stores/{id}/files/{path}", new AddedFile(path));
            case AddResult.Exists:
                return Errors.Conflict("file-exists");
            default:
                return Errors.TooLarge();
        }
    }

    /// <summary>Why the caller may not use <paramref name="store"/> with the <paramref name="needed"/> role; null when they may.</summary>
    private static IResult? Refuse(HttpContext context, Store? store, Role needed) =>
        store switch
        {
            null => Errors.NotFound("no-store"),
            { Info.Deleted: not null } => Errors.Forbidden("store-deleted"),
            _ when store.RoleOf(Caller.Of(context).Id) is not { } role || role < needed => Errors.Forbidden("not-allowed"),
            _ => null,
        };
}
