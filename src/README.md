# DailyGroove Sync API

> ## ⚠️ DELIBERATELY VULNERABLE UNTIL HARDENED — DO NOT DEPLOY
> This API started as an intentionally insecure baseline so each flaw can be exploited, documented
> and then fixed **in place**, one stage at a time (see `../SECURITY-JOURNEY.md`; each stage's
> end state is a git tag, starting with `stage-0-baseline`). **Never run it on a public network
> or with real data** until the journey says the relevant stages are done. The password in
> `appsettings.json` has no credentials: the local connection string uses Windows authentication to LocalDB.

A local-first sync API for Daily Groove: ASP.NET Core minimal API (.NET 10), EF Core + SQL Server.

## Projects
| Project | Purpose |
|---|---|
| `DailyGroove.Sync.Contracts` | Shared DTOs + source-generated `System.Text.Json` context (trim-safe for WASM/MAUI) |
| `DailyGroove.Sync.Api` | The API: `POST /sync/push`, `GET /sync/pull?userId=&since=` |
| `DailyGroove.Sync.Api.Tests` | xUnit v3 integration tests (`WebApplicationFactory`, real SQL Server LocalDB) |

## Sync model
- Client-generated GUID ids on every record.
- Synced in the first pass: `Exercise`, `SessionRecord`, `ActivityResult`.
- Pull cursor = SQL Server `rowversion` (exposed as a `long`). Pulls are capped below
  `MIN_ACTIVE_ROWVERSION()` so a slow transaction can't be skipped, and are paged (`limit`, default and max
  500; keep pulling from the returned `cursor` while `hasMore` is true). Push returns no cursor — the client
  keeps its own.
- Soft deletes (`IsDeleted` tombstones) so deletions propagate.
- Last-write-wins on `UpdatedAtUtc`.

## Running the tests
Requires SQL Server LocalDB (`(localdb)\MSSQLLocalDB`). Each test class gets a throwaway database.
```
dotnet run --project DailyGroove.Sync.Api.Tests
dotnet run --project DailyGroove.Sync.Api.Tests -- -class DailyGroove.Sync.Api.Tests.SyncHappyPathTests
dotnet run --project DailyGroove.Sync.Api.Tests -- -class DailyGroove.Sync.Api.Tests.AttackTests
```
(`dotnet test` is not used — see project notes on the .NET 10 SDK.)

## Deliberate vulnerabilities
Each has an exploit in `AttackTests.cs` that asserts the *protected* behaviour, so it is **red while
the flaw exists** and turns green only once the protection is in place (then it stays as a regression
test). Run one stage at a time with `-- -trait "Stage=Authentication"`.

| # | Flaw | OWASP API Top 10 (2023) |
|---|---|---|
| 1 | No authentication; user id taken from body/query | API2 Broken Authentication |
| 2 | No ownership checks (read/overwrite/delete anyone's data) | API1 Broken Object Level Authorization |
| 3 | Client sets server-owned fields (`UserId`, `UpdatedAtUtc`) | API3 Broken Object Property Level Authorization |
| 4 | No validation or request size limits | API4 Unrestricted Resource Consumption |
| 5 | No rate limiting | API4 Unrestricted Resource Consumption |
| 6 | SQL password in `appsettings.json` | API8 Security Misconfiguration |
| 7 | CORS allows any origin | API8 Security Misconfiguration |
| 8 | Verbose errors (stack traces returned) | API8 Security Misconfiguration |

Note: SQL Server will not accept a client-supplied `rowversion`, so "set server-owned fields" is
demonstrated through the owner and the timestamp (a far-future `UpdatedAtUtc` permanently wins
last-write-wins).

See `../SECURITY-JOURNEY.md` for the staged hardening plan.
