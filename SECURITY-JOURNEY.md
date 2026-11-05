# Daily Groove Sync API — Security Journey

An ongoing log of hardening the sync API, one risk at a time. Every stage follows the same loop:

1. **Identify the risk** — what it is, why it matters, and where to study it.
2. **Prove it exists** — one or more tests that exploit it.
3. **Document it** — an entry here.
4. **Fix it.**
5. **Prove the fix** — re-run the *same* tests. They assert the protected behaviour, so they start
   red and turn green only when the fix works.

The tests become permanent regression tests.

## Where we are

| Stage | Topic | Test trait (`-trait "Stage=…"`) | Status |
|---|---|---|---|
| 0 | Baseline sync API (insecure on purpose) | — | ✅ Built. 10 sync tests green (7 protocol + 3 correctness); 12 security tests deliberately RED (as at the baseline tag) |
| Gate | Secrets hygiene (part A): no committed password | `SecretsHygiene` | ✅ Done — 11 security tests still RED, 10 sync tests green |
| 1 | Threat model | — (document) | ⏳ |
| 2 | Authentication | `Authentication` | ⏳ |
| 3 | Object-level authorization | `ObjectAuthorization` | ⏳ |
| 4 | Mass assignment and validation | `MassAssignmentAndValidation` | ⏳ |
| 5 | Secure configuration (CORS, errors, headers) | `Configuration` | ⏳ |
| 6 | Abuse limits | `AbuseLimits` | ⏳ |
| 7 | Infrastructure and secrets (part B) | (added later) | ⏳ |
| 8 | Pipeline | (added later) | ⏳ |
| 9 | Monitoring | (added later) | ⏳ |

Study order and certification mapping (SC-500, optional BSCP) are in `.claude/sync-api-kickoff.md`
and the study map below.

### Why this order
The original kickoff brief listed ten stages in a different order. They were re-sequenced by
dependency (decision recorded here):

- **Gate first:** remove the committed password before any real Azure resource exists or anything is
  pushed to a public repo, so a real secret can never land in history. It's tiny, so it also proves the loop.
- **Identity before everything that needs a caller (2 → 3 → 4, then 6):** ownership checks, "derive the
  owner from the token, ignore the client's `UserId`" and per-user throttling all need authentication first.
- **Configuration (5) before abuse limits (6):** quick, independent, and it stops error and CORS leaks early.
- **Secrets part B joins infrastructure (7):** a managed identity needs a deployed host to attach to.
- **Pipeline (8) after infrastructure:** scanning guards what already exists, including the IaC.
- **Monitoring last (9):** its job is to detect the earlier attacks.

Trait names carry no numbers, so the order lives only in this document.

---

## Study map

Two tracks feed the stages. **PortSwigger Web Security Academy** (free, hands-on labs) teaches how the
application-level attacks work. The **SC-500** study guide covers the Azure controls that fix or
detect them. SC-500 is
[Implementing End-to-End Security Controls for Cloud and AI Workloads](https://learn.microsoft.com/credentials/certifications/resources/study-guides/sc-500)
(pass mark 700). Its four domains: identity, access and governance (20–25%); secure storage, databases
and networking (25–30%); secure compute (20–25%); manage and monitor security posture (20–25%).

PortSwigger topic pages: [Authentication](https://portswigger.net/web-security/authentication) ·
[OAuth](https://portswigger.net/web-security/oauth) · [JWT](https://portswigger.net/web-security/jwt) ·
[Access control](https://portswigger.net/web-security/access-control) ·
[API testing](https://portswigger.net/web-security/api-testing) ·
[CORS](https://portswigger.net/web-security/cors) ·
[Information disclosure](https://portswigger.net/web-security/information-disclosure) ·
[Race conditions](https://portswigger.net/web-security/race-conditions) ·
[Business logic](https://portswigger.net/web-security/logic-flaws)

| Stage | OWASP API 2023 · CWE | PortSwigger | SC-500 skills (domain) |
|---|---|---|---|
| Gate Secrets hygiene (A) | API8 · CWE-798 | Information disclosure | Key Vault block basics; see study order below |
| 1 Threat model | all | [API testing → OWASP Top 10 alignment](https://portswigger.net/web-security/api-testing/top-10-api-vulnerabilities) | [Microsoft Threat Modeling Tool](https://learn.microsoft.com/azure/security/develop/threat-modeling-tool) (listed in the study resources; not a measured skill) |
| 2 Authentication | API2 · CWE-287, CWE-347 | Authentication, OAuth, JWT | Identity for applications (app registrations); OAuth permission grants and consent; authentication methods incl. MFA/passwordless; conditional access (domain 1) |
| 3 Object authorization | API1 · CWE-639, CWE-862 | Access control, [IDOR](https://portswigger.net/web-security/access-control/idor); OWASP [IDOR Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Insecure_Direct_Object_Reference_Prevention_Cheat_Sheet.html) | Azure RBAC role assignments and over-privileged access (domain 1) — *Azure-level RBAC only; app-level ownership checks are PortSwigger/OWASP* |
| 4 Mass assignment & validation | API3 · CWE-915, CWE-20, CWE-362 | API testing: [mass assignment](https://portswigger.net/web-security/api-testing#mass-assignment-vulnerabilities), [server-side parameter pollution](https://portswigger.net/web-security/api-testing/server-side-parameter-pollution); Business logic; [Race conditions](https://portswigger.net/web-security/race-conditions) (concurrent pushes) | API Management back-end API protection policies (domain 3) — partial |
| 5 Secure configuration | API8 · CWE-942, CWE-209 | CORS; Information disclosure (verbose errors) | Security controls for App Service / Functions; WAF (domain 3) |
| 6 Abuse limits | API4 · CWE-770, CWE-307 | Race conditions; Authentication (brute-force protection) *(verify coverage)* | API Management policies; Web Application Firewall (domain 3) |
| 7 Infrastructure and secrets (B) | API8 | — | **Key Vault block** (deploy, access, firewall, keys/secrets/certs, Defender CSPM secret scanning, Defender for Key Vault), **managed identities**, Azure SQL platform security, private endpoints, NSGs, Defender for Databases, security controls via infrastructure as code (domains 1–2) |
| 8 Pipeline | — | — | Least covered by SC-500 (IaC controls, Defender CSPM). Use OWASP DevSecOps and GitHub/CodeQL docs |
| 9 Monitoring | API9/10 context · CWE-778 | Burp, to generate the attack traffic the alerts must catch | Defender for Cloud, Microsoft Sentinel, Azure Monitor / KQL (domain 4) |

**Reading the map honestly**
- Stages 2–6 are mostly PortSwigger and OWASP territory. They build exploitation and fix skills
  that BSCP (Burp Suite Certified Practitioner) tests, but SC-500 barely measures them.
- Stages 7 and 9 (and the Gate) carry most of the SC-500 value, with parts of 2, 5 and 6. A sensible
  point to book SC-500 is after Stage 7 or 9.
- The project does *not* cover all of SC-500: servers/VMs, AI security, Sentinel data connectors,
  Security Copilot and more need separate study.
- IDOR has its own page inside Access control (linked in the Stage 3 row). It points to the general
  access control labs rather than a single lab, so work through that whole section.
- AI security (SC-500 domain 3) isn't covered; see "Possible later stages" below.

### Gate and Stage 7 study order (secrets)
Links checked against the live Microsoft Learn catalogue on 2026-11-05. Four older modules (*Manage secrets
in your server apps with Azure Key Vault*, *Implement Azure Key Vault*, *Implement managed identities*,
*Configure and manage secrets in Azure Key Vault*) have been retired: their URLs redirect to generic pages,
although Learn's search index still lists them. Use these instead:

1. [Best practices for protecting secrets](https://learn.microsoft.com/azure/security/fundamentals/secrets-best-practices)
   — the Gate lesson in one article: audit, avoid hardcoding, secret scanning, and "treat any secret found by
   scanning as compromised: revoke and replace".
2. [Managed identities for Azure resources — overview](https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview)
   (system- vs user-assigned, how tokens are obtained).
3. [Tutorial: use Key Vault with an Azure web app in .NET](https://learn.microsoft.com/azure/key-vault/general/tutorial-net-create-vault-azure-web-app)
   — .NET, managed identity, `DefaultAzureCredential`; needs a free Azure subscription.
4. Module: [Manage application secrets with Azure Key Vault](https://learn.microsoft.com/training/modules/manage-app-secrets-key-vault/)
   (intermediate, 8 units, about 74 min). Its exercise uses the Python SDK and AI-solution examples, but the
   concepts — retrieval with managed identity, versioning and rotation, caching — carry over to .NET.
5. Module: [Implement access management for Azure resources](https://learn.microsoft.com/training/modules/implement-access-management-for-azure-resources/)
   (advanced, 10 units) — managed identities plus Key Vault RBAC; SC-500 identity domain.
6. Learning path: [Secure Azure Key Vault with defense in depth for the cloud and AI workloads](https://learn.microsoft.com/training/paths/configure-key-vault-security/)
   (4 modules: *Configure and secure Azure Key Vault*; *Manage keys and secrets*; *Manage certificates and
   monitor*; *Protect Key Vault with Defender for Cloud*). This is the SC-500 "Secure secrets and keys by using
   Azure Key Vault" block almost line for line. It assumes you already know Key Vault, RBAC and managed
   identities, so do items 1–5 first. Best done around Stage 7.
7. Docs for the fix: [app secrets in development](https://learn.microsoft.com/aspnet/core/security/app-secrets),
   [passwordless Azure SQL](https://learn.microsoft.com/azure/azure-sql/database/azure-sql-dotnet-quickstart),
   [Secure your Key Vault](https://learn.microsoft.com/azure/key-vault/general/secure-key-vault)
   *(these three are ordinary docs articles; not re-checked in the 2026-11-05 pass)*.

The Gate itself needs only items 1 and 7 (user secrets). Items 2–6 are the Stage 7 groundwork.

---

## Conventions

### Tags keep the "before" state checkable
The vulnerable version lives in one evolving repo, so the vulnerable state would vanish once fixed.
Tag the commit at the end of every stage and link the tags in each entry's Before/After lines:
`stage-0-baseline`, `stage-gate-secrets`, `stage-2-authentication`, and so on. (Tags are created by
the repo owner; Claude Code's git access is read-only here.)

### CI: don't show a permanently red build
With deliberately failing tests, a public repo would look broken. Plan for two jobs once the pipeline exists
(Stage 8):
- **Main job** — runs the happy-path tests plus the traits of every *completed* stage. Must be green.
- **Known vulnerabilities job** — runs the traits of *pending* stages. Allowed to fail, clearly labelled.
  It shrinks as each stage lands, which makes progress visible.

Locally, until then: run `-class DailyGroove.Sync.Api.Tests.SyncHappyPathTests`, or one trait at a time.

### Gate entry notes (secrets hygiene)
- Deleting a password from the current files doesn't remove it from Git history. For a real leaked
  secret the fix is to **rotate it**; deletion alone isn't enough.
- Keep the baseline password a throwaway, never used anywhere else. If it had been real, rotation
  would be the remedy; say so in the entry.
- GitHub push protection may flag the baseline when it is pushed. Record that in the entry instead of
  working around it quietly.

### Planned test setups
- **Stage 2 (authentication):** tests must not depend on a live Entra tenant. Give
  `WebApplicationFactory` a test signing key and issuer so tests mint their own tokens. That allows the JWT
  attacks as tests: unsigned token (`alg: none`), wrong audience, wrong issuer, expired token, and a token
  signed with a different key. Also add authenticated-attacker variants of the existing tests.
- **Stage 3 (object authorization):** ownership checks must cover *references between records*, not only
  the records themselves. `Record_cannot_reference_another_users_record` (an attacker's `ActivityResult`
  pointing at a victim's `SessionRecordId`) is already written and red — a BOLA variant with its own test.
- **Stage 4 (mass assignment and validation):** concurrency. Last-write-wins plus `rowversion` means two
  simultaneous pushes of the same record is a business-logic question. Add a race-condition test (see
  PortSwigger's Race conditions topic). Revisit under Stage 6 for abuse of the same path.
- **Stage 4, timestamp fix — record the trade-off.** If the server stamps `UpdatedAtUtc` on arrival, the
  meaning changes from "last edit wins" to "last sync wins", which hurts offline edits. Preferred design:
  keep the client's edit time but clamp it to at most server time plus a small allowance for clock skew.
  The existing test accepts either clamping or rejection.
- **Stage 6 (abuse limits):** the throttling test lives in its own class (`AbuseLimitTests`) with its own
  factory, so the 200-request burst can't starve neighbouring tests once rate limiting is on.
- **Stage 7 (infrastructure and secrets B):** move `Database.Migrate()` out of app startup. At startup the
  app's identity needs DDL rights; with a managed identity the app should hold only `db_datareader` and
  `db_datawriter`, with migrations run from the pipeline. A clean least-privilege lesson and SC-500 material.
- **Gate — the test is easy to bypass.** It only checks `appsettings.json`, so moving the password into
  `appsettings.Development.json` would turn it green. Keep the test, but treat secret scanning in the
  pipeline (Stage 8) as the real control.

### Possible later stages
- **AI/LLM security:** if Daily Groove ever gets an LLM feature (for example workout suggestions), add a
  stage with the same loop, covering the SC-500 AI material and PortSwigger's
  [Web LLM attacks](https://portswigger.net/web-security/llm-attacks).

---

## Stage 0 — Baseline

**State:** a working sync API with eight known flaws (catalogued in `src/README.md`).
**Proof the flaws exist:** 12 tests (11 in `AttackTests`, 1 in `AbuseLimitTests`) that assert the protected behaviour and therefore
**fail** against the baseline. Tagged by stage (`-trait "Stage=…"`): Authentication (1),
ObjectAuthorization (3), MassAssignmentAndValidation (3), Configuration (3), AbuseLimits (1),
SecretsHygiene (1).
**Proof the sync works:** `SyncHappyPathTests` — 7 tests (push/pull round trip, cursor, tombstones,
last-write-wins both ways, per-user pull).

## Gate — Secrets hygiene (part A)

**Risk:** a database credential committed in `appsettings.json`. **OWASP:** API8:2023 Security
Misconfiguration. **CWE:** CWE-798 (hard-coded credentials).
**Study:** [Best practices for protecting secrets](https://learn.microsoft.com/azure/security/fundamentals/secrets-best-practices);
[app secrets in development](https://learn.microsoft.com/aspnet/core/security/app-secrets); PortSwigger
[Information disclosure](https://portswigger.net/web-security/information-disclosure).
**Attack test:** `Database_password_is_not_committed_in_appsettings_json` (trait `SecretsHygiene`). It scans
every `appsettings*.json` in the output folder for `Password=`, `Pwd=` and `User Id=sa`, so moving the
password to `appsettings.Development.json` can't turn it green.
**Before:** tag `stage-0-baseline` — `appsettings.json` held `User Id=sa;Password=<placeholder>`. The test
failed with `appsettings.json must not contain a password`.
**Fix:** the local connection string now uses Windows authentication to LocalDB
(`Integrated Security=true`), the same server the tests use. There is no credential in the file at all, so
nothing needs hiding. If a real password is ever needed locally, use `dotnet user-secrets` (or an
environment variable), never a committed file.
**After:** the test passes; the 10 sync tests still pass; the 11 other security tests remain red.
Tag `stage-gate-secrets` (to be created by the repo owner after committing).
**What I learned / still open:**
- **Deleting a secret from the current file does not remove it from Git history.** The baseline placeholder
  is still in history (commit `c9ab3d0`, and so in the `stage-0-baseline` tag). That is harmless here because
  it was a throwaway value, never used anywhere else. If it had been real, the fix would be to **rotate it**
  (treat it as compromised, replace it), not just delete it.
- Do not rewrite history to hide it: the before/after story is the point of this repo.
- The test is a tripwire, not a control. The real control is secret scanning in the pipeline (Stage 8), plus
  watching for GitHub push protection firing on the first public push (record that here when it happens).
- Part B (no password at all in production: managed identity, Key Vault, migrations out of startup) belongs
  to Stage 7.

### Sync-correctness fixes made in the baseline
These bugs have nothing to do with the deliberate flaws, so they were fixed here to avoid carrying them
into the secured version (tests in `SyncCorrectnessTests`):

| Bug | Fix |
|---|---|
| Pulls could permanently miss rows: `rowversion` is assigned at write time, not commit time, so a slow transaction can commit after a later one | Pull returns only rows below `MIN_ACTIVE_ROWVERSION()`; the cursor is capped just under it |
| Push response cursor could skip another device's changes | Push no longer returns a cursor; the client keeps its own and pulls |
| `MaxVersionAsync` loaded every row version the user owns, three times, on each push | Removed with the push cursor |
| One query per record in each upsert | One `Contains(id)` query per table per batch |
| `datetime2` came back `Kind=Unspecified` (no `Z` on the wire) | UTC value converter on every `DateTime` |
| Pull was unbounded | Page size (default and max 500, optional `limit`) plus `hasMore` — also the first API4 control |

### Entry template for later stages

```
## Stage N — <topic>
**Risk:** …            **OWASP:** APIx:2023 …            **CWE:** CWE-nnn
**Study:** <links>
**Attack tests:** <test names> — proves …
**Before:** attack result (status code / data exposed) — tag `stage-<previous>`
**Fix:** what changed and where
**After:** the same tests now pass — tag `stage-<this>`; link to the commit/PR
**What I learned / still open:** …
```
