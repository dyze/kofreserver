using Kofre.Server.Accounts;
using Kofre.Server.Stores;

namespace Kofre.Server.Api;

public sealed record MemberRole(Role Role);

public sealed record FenceValue(long Value);

/// <summary>
/// A store's members, and its fence. The owner, its creator, adds readers and writers, changes
/// their role or removes them, and sets the fence; a member may leave. The owner is never changed
/// nor removed.
/// </summary>
internal static class MemberEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var store = api.MapGroup("/stores/{id:guid}").RequireAuthorization().AddEndpointFilter(Caller.RequireActive);
        store.MapGet("/members", List);
        store.MapPut("/members/{account:guid}", Set);
        store.MapDelete("/members/{account:guid}", Remove);
        store.MapDelete("/members/me", Leave);
        store.MapPut("/fence", SetFence);
        store.MapDelete("/fence", RemoveFence);
    }

    private static IResult List(Guid id, HttpContext context, StoreRepository stores)
    {
        var store = stores.Find(id);
        return StoreEndpoints.Refuse(context, store, Role.Reader)
            ?? Results.Ok(store!.Info.Members.Select(member => new Member(member.Key, member.Value)).ToList());
    }

    private static IResult Set(Guid id, Guid account, MemberRole request, HttpContext context, StoreRepository stores, AccountStore accounts, ILogger<Store> log)
    {
        var store = stores.Find(id);
        if (StoreEndpoints.Refuse(context, store, Role.Owner) is { } refused)
        {
            return refused;
        }

        if (request.Role == Role.Owner)
        {
            return Errors.BadRequest("invalid-role");
        }

        if (store!.RoleOf(account) == Role.Owner)
        {
            return Errors.Conflict("owner-fixed");
        }

        if (accounts.Find(account) is not { IsActive: true })
        {
            return Errors.NotFound("no-account");
        }

        store.SetMember(account, request.Role);
        log.LogInformation("Store {Store}: {Member} made {Role} by {Account}", id, account, request.Role, Caller.Of(context).Id);
        return Results.NoContent();
    }

    private static IResult Remove(Guid id, Guid account, HttpContext context, StoreRepository stores, ILogger<Store> log)
    {
        var store = stores.Find(id);
        if (StoreEndpoints.Refuse(context, store, Role.Owner) is { } refused)
        {
            return refused;
        }

        switch (store!.RoleOf(account))
        {
            case null:
                return Errors.NotFound("not-member");
            case Role.Owner:
                return Errors.Conflict("owner-fixed");
        }

        store.SetMember(account, null);
        log.LogInformation("Store {Store}: {Member} removed by {Account}", id, account, Caller.Of(context).Id);
        return Results.NoContent();
    }

    private static IResult Leave(Guid id, HttpContext context, StoreRepository stores, ILogger<Store> log)
    {
        var store = stores.Find(id);
        if (StoreEndpoints.Refuse(context, store, Role.Reader) is { } refused)
        {
            return refused;
        }

        var account = Caller.Of(context).Id;
        if (store!.RoleOf(account) == Role.Owner)
        {
            return Errors.Conflict("owner-cannot-leave");
        }

        store.SetMember(account, null);
        log.LogInformation("Store {Store}: {Account} left", id, account);
        return Results.NoContent();
    }

    private static IResult SetFence(Guid id, FenceValue request, HttpContext context, StoreRepository stores, ILogger<Store> log)
    {
        var store = stores.Find(id);
        if (StoreEndpoints.Refuse(context, store, Role.Owner) is { } refused)
        {
            return refused;
        }

        if (request.Value < 0)
        {
            return Errors.BadRequest("invalid-fence");
        }

        if (!store!.SetFence(request.Value))
        {
            return Errors.Fenced("fence-lower", store.Info.Fence);
        }

        log.LogInformation("Store {Store}: fence set to {Fence} by {Account}", id, request.Value, Caller.Of(context).Id);
        return Results.NoContent();
    }

    private static IResult RemoveFence(Guid id, HttpContext context, StoreRepository stores, ILogger<Store> log)
    {
        var store = stores.Find(id);
        if (StoreEndpoints.Refuse(context, store, Role.Owner) is { } refused)
        {
            return refused;
        }

        store!.SetFence(null);
        log.LogInformation("Store {Store}: fence turned off by {Account}", id, Caller.Of(context).Id);
        return Results.NoContent();
    }
}
