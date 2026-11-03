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
build\build.cmd
```

Builds and tests (`build/build.sh` on Linux and macOS); fails below 95 % line coverage. Needs the .NET 10 SDK. A server started from `artifacts\` is stopped first, as it locks its files.

## Run

```
build\installer.cmd
```

Builds `artifacts\installer\kofre-server-<version>-linux-x64.zip`: a `kofre` folder with the server as one file, `Kofre.Server` (self-contained: no .NET runtime needed on the machine), its `appsettings.json`, the samples and these docs.

```
Internet ──HTTPS──► Caddy (:80, :443) ──HTTP──► Kofre.Server (127.0.0.1:5080) ──► /var/lib/kofre
```

### Install (Ubuntu)
Needs a DNS name pointing to the server's public address, and ports 80 and 443 open to it (firewall, router): Caddy gets its certificate from Let's Encrypt through them.

1. Copy the zip to the server (`scp`), then:
   ```
   sudo apt update && sudo apt install -y unzip openssl caddy
   sudo unzip kofre-server-<version>-linux-x64.zip -d /opt
   sudo chmod +x /opt/kofre/Kofre.Server
   sudo useradd --system --no-create-home kofre
   sudo mkdir -p /var/lib/kofre && sudo chown kofre /var/lib/kofre
   sudo mkdir -p /var/backups/kofre && sudo chown kofre /var/backups/kofre && sudo chmod 700 /var/backups/kofre
   ```
   The zip keeps no Unix permissions: `chmod +x` makes the server runnable.
2. Make the [signing key](#signing-key).
3. Start the service, then check it answers `{"version":"<version>"}`:
   ```
   sudo cp /opt/kofre/samples/kofre.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now kofre
   curl http://127.0.0.1:5080/v1/version
   ```
4. HTTPS: copy [`samples/Caddyfile`](samples/Caddyfile) to `/etc/caddy/Caddyfile`, **replace `kofre.example.org` with the server's DNS name**, then `sudo systemctl reload caddy`. With `ufw`: `sudo ufw allow 80,443/tcp`. Check `https://<name>/v1/version` from outside. A browser error such as `SSL_ERROR_INTERNAL_ERROR_ALERT` means Caddy has no certificate for that name: wrong name in the Caddyfile, DNS, or port 80 closed (`journalctl -u caddy`).
5. Sign up from a client, then [approve](#admin) the account in `/var/lib/kofre/accounts.json`.

Logs: `journalctl -u kofre -f`, or the files in `/var/log/kofre`. **Upgrade**: `sudo systemctl stop kofre`, unzip the new version over `/opt/kofre` (`unzip -o`), `chmod +x` again, `sudo systemctl start kofre`. The data in `/var/lib/kofre` is kept.

### Backup and restore
Every day at `Backup:Time` (03:00), the server writes `/var/backups/kofre/kofre-<date>.zip`: the data folder under `data/`, and the files of `Backup:ConfigFiles` under `config/` (`appsettings.json`, `secrets.env`). Meanwhile it is in **maintenance**: writes are refused (`503 maintenance`), reads still work. It first waits for the writes already running (`Backup:WritesTimeout`); past it, the backup is skipped.

| Kept | Default |
| --- | --- |
| The latest archive of each of the last days | 7 (`KeepDaily`) |
| … of each of the last weeks | 4 (`KeepWeekly`) |
| … of each of the last months | 12 (`KeepMonthly`) |

The others are deleted after each backup: 23 archives at most. A failed backup is logged as an error and [emailed](#emails-to-the-admin).

Copy the archives off the server (`rsync`, `scp`). They hold password hashes, the signing key and the SMTP password: store them as safely as the server; the clients' files in them are encrypted by them.

Restore, on a server installed as above with an empty `/var/lib/kofre` (steps 1, 3 and 4; the key comes from the archive):

```
sudo systemctl stop kofre
sudo unzip kofre-<date>.zip -d /tmp/kofre-restore
sudo cp -a /tmp/kofre-restore/data/. /var/lib/kofre/
sudo cp /tmp/kofre-restore/config/appsettings.json /opt/kofre/
sudo mkdir -p /etc/kofre && sudo cp /tmp/kofre-restore/config/secrets.env /etc/kofre/
sudo chown -R kofre /var/lib/kofre
sudo chown root:kofre /etc/kofre/secrets.env && sudo chmod 640 /etc/kofre/secrets.env
sudo rm -r /tmp/kofre-restore
sudo systemctl start kofre
```

What was uploaded after the backup is not on the server any more. A lost signing key is not lost data: make a new one; devices renew their access with their refresh token.

### Signing key
32 random bytes in base64, made once per server. Whoever has it can sign in as any account: keep it out of `appsettings.json` and of the repo. Changing it ends every access token at once; devices renew theirs with their refresh token, without signing in again.

On the server (Linux), into the file `kofre.service` reads:

```
sudo mkdir -p /etc/kofre
echo "Kofre__SigningKey=$(openssl rand -base64 32)" | sudo tee /etc/kofre/secrets.env > /dev/null
sudo chown root:kofre /etc/kofre/secrets.env
sudo chmod 640 /etc/kofre/secrets.env
```

To try the server on Windows (PowerShell 5.1 or 7), for this shell only:

```
$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$env:Kofre__SigningKey = [Convert]::ToBase64String($bytes)
dotnet run --project src/Kofre.Server
```

## Configuration
The `Kofre` section of `appsettings.json`, or environment variables (`Kofre__TrashDays=7`).

| Setting | Default | |
| --- | --- | --- |
| `SigningKey` | *(required)* | Signs access tokens: base64, at least 32 bytes. The server does not start without it |
| `DataFolder` | `data` | Where accounts, tokens and stores are kept |
| `TrashDays` | 30 | Days a deleted store stays in the trash before it is purged |
| `MaxUploadBytes` | 4 GB | Largest file accepted |
| `MaxPendingAccounts` | 10 | Accounts waiting for approval at once: more sign-ups are refused (`sign-up-closed`) until the admin approves or disables some. 0 closes sign-ups |
| `SignInsPerMinute` | 10 | Sign-ups, sign-ins and refreshes per minute and client address |
| `LookupsPerMinute` | 10 | Accounts looked up by email per minute and account |
| `MaxAccountDataBytes` | 64 KB | Largest public or private part of an account's data |
| `PasswordIterations` | 600 000 | PBKDF2-SHA256 iterations of new password hashes |
| `AccessTokenLifetime`, `RefreshTokenLifetime` | 15 min, 30 days | |

**Backup** (`Backup:…`, see [Backup and restore](#backup-and-restore)):

| Setting | Default | |
| --- | --- | --- |
| `Enabled` | true | The daily backup |
| `Folder` | `backups` | Where the archives are written, outside the data folder |
| `Time` | `03:00` | The server's local time of the backup |
| `ConfigFiles` | `appsettings.json` | Files archived under `config/`; a missing one is logged and skipped |
| `WritesTimeout` | 10 min | How long the backup waits for the writes already running |
| `KeepDaily`, `KeepWeekly`, `KeepMonthly` | 7, 4, 12 | Archives kept |

**Logs** (`Logs:…`), besides the console:

| Setting | Default | |
| --- | --- | --- |
| `Folder` | `logs` | Where `kofre.log` is written; empty: no file |
| `MaxFiles`, `MaxFileBytes` | 5, 10 MB | Past its size, `kofre.log` becomes `kofre.1.log`, and so on; the oldest is deleted. 50 MB at most |
| `MemoryEntries` | 1000 | Latest entries kept in memory, for the daily summary |

### Emails to the admin
`Email:…`, through any SMTP server. Off by default. Put the password in `/etc/kofre/secrets.env` (`Kofre__Email__Password=…`), not in `appsettings.json`.

| Setting | Default | |
| --- | --- | --- |
| `Enabled` | false | |
| `Host`, `Port` | , 587 | The SMTP server |
| `Security` | `StartTls` | `StartTls`, `SslOnConnect` (port 465), or `None` (an SMTP server on this machine only) |
| `User`, `Password` | | Empty user: no sign-in |
| `From`, `To` | | The sender, and the admin's address |
| `SummaryTime` | `07:00` | The server's local time of the daily summary |

| Email | When |
| --- | --- |
| started | The server starts. *after an unclean stop* when the last run did not stop (crash, power loss): no stopping email came |
| stopping | The server stops |
| account waiting for approval | A sign-up |
| backup failed | With the reason |
| daily summary | Accounts (those waiting listed), stores, the last backup, and the warnings and errors logged since the last summary (50 at most) |

A failed email is logged as a warning; the server goes on.

## Admin
No admin UI: the admin edits the JSON files, read again at every request (no restart).

| To | Edit |
| --- | --- |
| Approve an account | `accounts.json`: `"approved": true` |
| Disable an account (at once, tokens included), or reject one waiting | `accounts.json`: `"disabled": true` |
| Restore a store from the trash | `stores/<id>/store.json`: remove `"deleted"` |

```
<DataFolder>/
  server.running          there while the server runs: found at start, the last stop was unclean
  accounts.json           [{ id, email, passwordHash, created, approved, disabled }]
  tokens.json             refresh tokens, hashed (SHA-256), with their account and expiry
  account-data/<id>.json  { version, public, private, updated }: an account's data, opaque
  stores/<id>/
    store.json            { members: { <account>: owner | writer | reader }, created, deleted?, fence? }
    feed.jsonl            one line per file added: { seq, path, size }
    files/…               the clients' files, by path
    incoming/             uploads being received
```

- A file is streamed to `incoming/`, moved to its path, then added to the feed. A crash between both leaves a file missing from the feed: it is added when it is uploaded again.
- Logs: the console (journald under systemd) and the [log files](#configuration). Never a password, a token or a file's content.

## API (`/v1`)
JSON; errors are `{ "error": "<code>" }`. Every call but the version, status, sign-up, sign-in and refresh needs `Authorization: Bearer <access token>`; access to a store is checked at every request against its members.

| Method and path | Does | Errors |
| --- | --- | --- |
| `GET /version` | The server's version: `{version}`, without signing in | |
| `GET /status` | `{maintenance}`, without signing in: true during a backup | |
| `POST /accounts` `{email, password}` | Sign up: `202`, waits for approval | `invalid-email`, `weak-password` (under 10 characters), `email-taken`, `sign-up-closed` (403) |
| `POST /sessions` `{email, password}` | Sign in: `{accessToken, accessExpires, refreshToken}` | `wrong-credentials` (401), `account-not-approved`, `account-disabled` (403) |
| `POST /sessions/refresh` `{refreshToken}` | A new session; the refresh token is used once | `session-ended` (401), `account-inactive` (403) |
| `PUT /accounts/me/password` `{password}` + `X-Password` | Changes the password, ends the other sessions | `wrong-password`, `weak-password` |
| `GET /accounts/me/data` | The caller's data, opaque: `{version, public, private, updated}`, the parts in base64; version 0 when none | |
| `PUT /accounts/me/data` `{version, public, private}` | Replaces it when `version` is still the current one: `{version}`, the next one | `data-changed` (409, with the current `version`), `invalid-data`, `too-large` (413) |
| `GET /accounts?email=` | An active account, by its exact email (any case): `{id, public}`. Limited per account (`LookupsPerMinute`) | `no-account` (404), `429` |
| `GET /accounts/{id}/public` | An active account's public part: `{public}` | `no-account` (404) |
| `GET /stores` | The stores the caller is a member of: `[{id, role, files, bytes, fence}]` | |
| `POST /stores` | Creates a store, the caller owner: `201 {id}` | |
| `GET /stores/{id}/stats` | `{files, bytes}` | |
| `GET /stores/{id}/feed?since={seq}` | Files added after `seq`: `{entries: [{seq, path, size}], more}`, 1000 at most | |
| `GET /stores/{id}/files/{path}` | Downloads a file | `no-file` |
| `PUT /stores/{id}/files/{path}` | Uploads a file (writer): `201`. Once the store's fence is on, the `Store-Fence` header must name a number at least as high | `file-exists` (409), `fence` (409, `{error, fence}`), `invalid-path`, `too-large` (413) |
| `DELETE /stores/{id}` + `X-Password` | To the trash (owner): `204` | `wrong-password` |
| `POST /stores/{id}/restore` + `X-Password` | Back from the trash (owner): `204` | `not-deleted` |
| `GET /stores/{id}/members` | Its members (any member): `[{account, role}]` | |
| `PUT /stores/{id}/members/{account}` `{role}` | Adds an active account as `reader` or `writer`, or changes its role (owner) | `no-account` (404), `owner-fixed` (409), `invalid-role` |
| `DELETE /stores/{id}/members/{account}` | Removes a member (owner): refused at its next request | `not-member` (404), `owner-fixed` (409) |
| `DELETE /stores/{id}/members/me` | Leaves the store | `owner-cannot-leave` (409) |
| `PUT /stores/{id}/fence` `{value}` | Turns the fence on, or raises it (owner): never lowered | `fence-lower` (409, `{error, fence}`), `invalid-fence` |
| `DELETE /stores/{id}/fence` | Turns the fence off (owner) | |

Store errors: `no-store` (404), `not-allowed`, `store-deleted` (403), `account-inactive` (403, any call). Too many sign-ins: `429`.

**Maintenance** (during a backup): every call but `GET` is refused with `503 maintenance` and `Retry-After: 60`, sign-ins and refreshes included. Reads still work. Retry later, or poll `GET /status`.

The **fence** is an optional number per store, for clients that must stop writers that are behind: the server only compares it with the `Store-Fence` header, and knows nothing of what it means. Off, the header is ignored.

Paths: segments of letters, digits, `.`, `_` and `-`, separated by `/` (16 at most, 512 characters).

## Tests
`tests/Kofre.Server.Tests`: xUnit, the API on an in-memory server (`Microsoft.AspNetCore.Mvc.Testing`). Line coverage ≥ 95 %, enforced by `build\build.cmd`.

Dependencies: ASP.NET Core (shared framework), `Microsoft.AspNetCore.Authentication.JwtBearer`, `MailKit` (emails; `System.Security.Cryptography.Pkcs` pinned to a patched version for it); for the tests `Microsoft.AspNetCore.Mvc.Testing`, xUnit, `coverlet.msbuild`, and `dotnet-reportgenerator-globaltool` (local tool, `.config/dotnet-tools.json`).
