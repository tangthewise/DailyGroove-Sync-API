# DailyGroove Sync API — Unsecured Baseline

> ## ⚠️ DELIBERATELY VULNERABLE — DO NOT DEPLOY
> This API is intentionally insecure so each flaw can be exploited, documented and then fixed
> in the secured copy. **Never run it on a public network or with real data.** The password in
> `appsettings.json` is a fake placeholder for demonstration.

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
- Pull cursor = SQL Server `rowversion` (exposed as a `long`).
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
Each has an exploit in `AttackTests.cs` that **passes while the flaw exists**. In the secured copy
the same tests are inverted into regression tests proving the attack fails.

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
