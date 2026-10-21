# Kofre server

A generic store of files for apps that encrypt their own data: accounts approved by an admin,
**stores** (sets of files addressed by path) with their members, a REST API with tokens. A file,
once uploaded, is never replaced. The server never sees a key or a content, and knows nothing
of its clients. What each version changed: [CHANGELOG.md](CHANGELOG.md).

## Principles
- **Generic and blind.** Only accounts, stores, members, files and their feed: no client's term in the API, code, files or docs. Paths and contents are opaque: no format read, no file name pattern assumed.
- **Write-once.** An upload on an existing path is refused. Only a whole store can be deleted, into a trash first.
- **Clients talk to it over HTTP only**, through the versioned API (`/v1`): no shared code, so server and clients evolve apart.
- **REST only.** No web site, no admin UI: the admin edits the JSON files.

## Build

```
build.cmd
```

Builds and tests (`build.sh` on Linux and macOS); fails below 95 % line coverage. Needs the .NET 10 SDK.

## Run

```
dotnet publish src/Kofre.Server -c Release -r linux-x64 --self-contained -o publish/kofre
```

Copy `publish/kofre` to `/opt/kofre`, then use the samples in [`samples`](samples):
`kofre.service` (systemd) and `Caddyfile` (HTTPS). Put the signing key in `/etc/kofre/secrets.env`:

```
Kofre__SigningKey=<openssl rand -base64 32>
```

## Configuration
The `Kofre` section of `appsettings.json`, or environment variables (`Kofre__TrashDays=7`).

| Setting | Default | |
| --- | --- | --- |
| `SigningKey` | *(required)* | Signs access tokens: base64, at least 32 bytes. The server does not start without it |
| `DataFolder` | `data` | Where accounts, tokens and stores are kept |
| `TrashDays` | 30 | Days a deleted store stays in the trash before it is purged |
| `MaxUploadBytes` | 4 GB | Largest file accepted |
| `SignInsPerMinute` | 10 | Sign-ups, sign-ins and refreshes per minute and client address |
| `PasswordIterations` | 600 000 | PBKDF2-SHA256 iterations of new password hashes |
| `AccessTokenLifetime`, `RefreshTokenLifetime` | 15 min, 30 days | |

## Admin
No admin UI: the admin edits the JSON files, read again at every request (no restart).

| To | Edit |
| --- | --- |
| Approve an account | `accounts.json`: `"approved": true` |
| Disable an account (at once, tokens included) | `accounts.json`: `"disabled": true` |
| Restore a store from the trash | `stores/<id>/store.json`: remove `"deleted"` |

```
<DataFolder>/
  accounts.json           [{ id, email, passwordHash, created, approved, disabled }]
  tokens.json             refresh tokens, hashed (SHA-256), with their account and expiry
  stores/<id>/
    store.json            { members: { <account>: owner | writer | reader }, created, deleted? }
    feed.jsonl            one line per file added: { seq, path, size }
    files/…               the clients' files, by path
    incoming/             uploads being received
```

- A file is streamed to `incoming/`, moved to its path, then added to the feed. A crash between both leaves a file missing from the feed: it is added when it is uploaded again.
- Logs: the console (journald under systemd). Never a password, a token or a file's content.

## API (`/v1`)
JSON; errors are `{ "error": "<code>" }`. Every call but sign-up, sign-in and refresh needs `Authorization: Bearer <access token>`; access to a store is checked at every request against its members.

| Method and path | Does | Errors |
| --- | --- | --- |
| `POST /accounts` `{email, password}` | Sign up: `202`, waits for approval | `invalid-email`, `weak-password` (under 10 characters), `email-taken` |
| `POST /sessions` `{email, password}` | Sign in: `{accessToken, accessExpires, refreshToken}` | `wrong-credentials` (401), `account-not-approved`, `account-disabled` (403) |
| `POST /sessions/refresh` `{refreshToken}` | A new session; the refresh token is used once | `session-ended` (401), `account-inactive` (403) |
| `PUT /accounts/me/password` `{password}` + `X-Password` | Changes the password, ends the other sessions | `wrong-password`, `weak-password` |
| `GET /stores` | The caller's stores: `[{id, role, files, bytes}]` | |
| `POST /stores` | Creates a store, the caller owner: `201 {id}` | |
| `GET /stores/{id}/stats` | `{files, bytes}` | |
| `GET /stores/{id}/feed?since={seq}` | Files added after `seq`: `{entries: [{seq, path, size}], more}`, 1000 at most | |
| `GET /stores/{id}/files/{path}` | Downloads a file | `no-file` |
| `PUT /stores/{id}/files/{path}` | Uploads a file (writer): `201` | `file-exists` (409), `invalid-path`, `too-large` (413) |
| `DELETE /stores/{id}` + `X-Password` | To the trash (owner): `204` | `wrong-password` |
| `POST /stores/{id}/restore` + `X-Password` | Back from the trash (owner): `204` | `not-deleted` |

Store errors: `no-store` (404), `not-allowed`, `store-deleted` (403), `account-inactive` (403, any call). Too many sign-ins: `429`.

Paths: segments of letters, digits, `.`, `_` and `-`, separated by `/` (16 at most, 512 characters).

## Tests
`tests/Kofre.Server.Tests`: xUnit, the API on an in-memory server (`Microsoft.AspNetCore.Mvc.Testing`). Line coverage ≥ 95 %, enforced by `build.cmd`.

Dependencies: ASP.NET Core (shared framework), `Microsoft.AspNetCore.Authentication.JwtBearer`; for the tests `Microsoft.AspNetCore.Mvc.Testing`, xUnit, `coverlet.msbuild`, and `dotnet-reportgenerator-globaltool` (local tool, `.config/dotnet-tools.json`).
