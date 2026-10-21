# CLAUDE.md

Rules for working on **kofreserver** together. Read first:
- `README.md`: principles, build, run, configuration, admin, API
- `CHANGELOG.md`: what each version changed
- `ROADMAP.md`: planned versions and ideas

## Project in one line
Kofre: a generic, blind, write-once store of files with accounts and members, behind a REST API, for apps that encrypt their own data.

## Architecture rules (keep it this way)
- **Generic.** No client's term (no vault, node, key epoch...) in the API, code, files or docs: only accounts, stores, members, files, feed. Paths and contents are opaque: never read a format or assume a file name pattern.
- **Blind.** The server never sees a key, a name or a content, and never needs one.
- **Write-once.** A file is never replaced or deleted; only a whole store is deleted, into a trash first.
- **Clients talk to it over HTTP only**, through the versioned API (`/v1/…`). A breaking change to the API gets a new version; tell the owner before making one.
- **Access is checked at every request** against the store's members and the account's state, not in the token.
- Every behavior a client may not want is optional per store.

## Dependencies
Only those listed in `README.md#tests`. Ask before adding one.

## Logging rules
- Built-in `ILogger` only, to the console (journald under systemd).
- Every account or store action at **Information**, refusals at Warning when they matter, file transfers at **Debug**.
- Never log a password, a token, a refresh token, or a file's content.

## Versioning rules
- Format `MAJOR.MINOR`, starting at **0.1**.
- `<Version>` in `Directory.Build.props` is the only place the version is written: not in code, docs or scripts.
- Bump it only when the owner asks.

## Repo layout
`src/`, `tests/`, `samples/`, `build/` (release scripts: `installer.cmd`), `.config/` (dotnet tools), `artifacts/` (ignored). Do not add top-level folders without asking.

## How we work
- **Concise answers.** Short explanations, diagrams over long text.
- **Plan first.** Before changing any code, present an implementation plan for the developer to review. Change code only after the developer explicitly says to implement it.
- **Do it well.** Prefer the clean, simple solution over the quick hack.
- **Ask before** adding a dependency, changing the architecture, a breaking API change, or a large refactor.
- **Tests.** Every change comes with xUnit tests, through the API on the in-memory server where possible.
- **Build honesty.** Claude builds and runs the tests itself (`build.cmd`) and reports the real result, errors and coverage included. Never claim the code builds without having run it.
- **Docs.** One home per fact: behavior, configuration, admin and API → `README.md`; planned features → `ROADMAP.md`; changes → `CHANGELOG.md`, following [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) (`[Unreleased]`, then `[<version>] - <date>` at release). Prefer tables to long descriptions. Update them when behavior changes.
- **Don't touch** `LICENSE`.

## Coding rules
* Apply Clean Code principles: meaningful names rather than long comments.
* Write code for today's problems, not tomorrow's fantasies.
* Remove dead code; avoid redundant code.
* Keep files short: split large classes or methods into separate `.cs` files.
* Line coverage **>= 95%**, enforced by `build.cmd`.
* Reduce build warnings.

## Git
- Commit and push only when asked.
- Create branches only when asked. Otherwise work on `main`.
- Small commits, [Conventional Commits](https://www.conventionalcommits.org/en) with an imperative subject (`feat: add store members`).
- No AI attribution in commit messages or PR descriptions (no `Co-Authored-By: Claude …`, no "Generated with Claude Code").

## Definition of done
Code is written, tests cover the new behavior, docs are updated, and `build.cmd` passes.
