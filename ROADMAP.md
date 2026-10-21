# Kofre server: roadmap

Scope of each planned version. Implemented features move to [CHANGELOG.md](CHANGELOG.md). The features are sorted by priority.

| Version | Status    | Theme                                          |
| ------- | --------- | ---------------------------------------------- |
| 0.1     | Released  | Accounts, stores, trash                        |
| 0.2     | To define | Members: account data, shared stores, fence    |

## 0.2 (to define)
1. **Account data.** Opaque data per account: a public part any signed-in account reads (`GET /accounts/{id}/public`), a private part only its owner reads and writes (`GET/PUT /accounts/me/data`).
2. **Find an account** by its exact email (`GET /accounts?email=`): its id only, no partial match or listing.
3. **Members.** The owner adds a member as reader or writer, changes their role or removes them (`PUT/DELETE /stores/{id}/members/{account}`); a member leaves (`DELETE /stores/{id}/members/me`). One owner per store, its creator: not changed, not removed. `GET /stores` lists shared stores too.
4. **Fence.** An optional number per store: the owner turns it on, raises it (never lowers it) or turns it off (`PUT/DELETE /stores/{id}/fence`); once on, an upload needs a `Store-Fence` header at least as high (`409` otherwise, with the current fence).
5. **Version.** `GET /version` returns the server's version, so a client can say the server is too old.

## Ideas
1. **Propose a Server Admin App.**
