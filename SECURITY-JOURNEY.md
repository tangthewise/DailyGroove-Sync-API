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

| Stage | Topic | Status |
|---|---|---|
| 0 | Baseline sync API (insecure on purpose) | ✅ Built. 7 sync tests green; 11 security tests deliberately RED (one per weakness) |
| 1 | Threat model | ⏳ Not started |
| 2 | Authentication | ⏳ |
| 3 | Object-level authorization | ⏳ |
| 4 | Mass assignment and validation | ⏳ |
| 5 | Abuse limits | ⏳ |
| 6 | Secrets | ⏳ |
| 7 | Configuration (CORS, errors, headers) | ⏳ |
| 8 | Infrastructure | ⏳ |
| 9 | Pipeline | ⏳ |
| 10 | Monitoring | ⏳ |

Study order and certification mapping (SC-500, optional BSCP) are in `.claude/sync-api-kickoff.md`.

---

## Stage 0 — Baseline

**State:** a working sync API with eight known flaws (catalogued in `Unsecured/README.md`).
**Proof the flaws exist:** `AttackTests` — 11 tests that assert the protected behaviour and therefore
**fail** against the baseline. Tagged by stage (`-trait "Stage=…"`): 2-Authentication (1),
3-ObjectAuthorization (2), 4-MassAssignmentAndValidation (3), 5-AbuseLimits (1), 6-Secrets (1),
7-Configuration (3).
**Proof the sync works:** `SyncHappyPathTests` — 7 tests (push/pull round trip, cursor, tombstones,
last-write-wins both ways, per-user pull).

### Entry template for later stages

```
## Stage N — <topic>
**Risk:** …            **OWASP:** APIx:2023 …
**Study:** <links>
**Attack tests:** <test names> — proves …
**Before:** attack result (status code / data exposed)
**Fix:** what changed and where
**After:** the same tests now pass; link to the commit/PR
**What I learned / still open:** …
```
