# Changelog

All notable changes to the Kofre server are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow a `MAJOR.MINOR` scheme, not Semantic Versioning.

## [Unreleased]

## [0.2] - 2026-10-24

### Added
- **Version**: `GET /version`, without signing in, so a client can tell the server is too old.
- **Account data**: opaque, a public part any signed-in account reads and a private part only its account reads. Each write names the version it replaces, so two devices cannot overwrite each other (`MaxAccountDataBytes`).
- **Find an account** by its exact email, limited per account (`LookupsPerMinute`), and read its public part by id. Only active accounts are found.
- **Members**: the owner adds active accounts as readers or writers, changes their role or removes them; a member leaves. The owner is never changed nor removed. Every member reads the members.
- **Fence**: an optional number per store, set by its owner and never lowered. Once on, an upload names one at least as high (`Store-Fence`), checked again as the file is added.

## [0.1] - 2026-10-21

### Added
- **Accounts**: sign up, approved by the admin in `accounts.json`, at most 10 waiting at once (`MaxPendingAccounts`); sign in with an access token (15 minutes) and a refresh token (30 days, used once); password change, ending the other sessions; sign-ins limited per minute and address.
- **Stores**: create and list, owner only for now; files uploaded once, never replaced; a feed of the files added; statistics.
- **Trash**: deleting a store (its owner's password asked again) moves it to the trash. It can be restored from there until it is purged, after 30 days by default (`TrashDays`).

[Unreleased]: https://github.com/dyze/kofreserver/compare/0.2...HEAD
[0.2]: https://github.com/dyze/kofreserver/compare/0.1...0.2
[0.1]: https://github.com/dyze/kofreserver/releases/tag/0.1
