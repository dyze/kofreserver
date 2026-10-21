# Changelog

All notable changes to the Kofre server are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow a `MAJOR.MINOR` scheme, not Semantic Versioning.

## [Unreleased]

### Added
- **Accounts**: sign up, approved by the admin in `accounts.json`, at most 10 waiting at once (`MaxPendingAccounts`); sign in with an access token (15 minutes) and a refresh token (30 days, used once); password change, ending the other sessions; sign-ins limited per minute and address.
- **Stores**: create and list, owner only for now; files uploaded once, never replaced; a feed of the files added; statistics.
- **Trash**: a store deleted (password asked again) stays in the trash, restorable, then is purged after 30 days.
