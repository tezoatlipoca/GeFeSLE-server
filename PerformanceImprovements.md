# Performance Improvement Notes

Date: 2026-06-27
Scope: Read-only review of hot paths in Program.cs and related request/session helpers. No code changes performed.

## Highest Priority Findings

1. Session access updates likely write to DB on most authenticated requests.
- Evidence:
  - UserSession.cs line 25 updates access time per request path.
  - UserSession.cs line 39 blocks on an async call via .Result in request flow.
  - Program.cs calls UpdateSessionAccessTime in many common routes (examples: lines 1394, 1446, 1653, 1678, 1783, 1837).
- Impact:
  - Frequent write amplification and blocking behavior can raise latency and reduce throughput, especially with SQLite.

2. Custom middleware performs substantial work before static files are served.
- Evidence:
  - Main middleware starts at Program.cs line 650.
  - Static file serving is configured later at Program.cs line 804.
  - Middleware includes per-request CORS checks, session checks, protected file checks, possible DB lookup, and logging.
- Impact:
  - Requests that could be cheap static responses still pay middleware overhead first.

3. Global request logging on hot path at Information level.
- Evidence:
  - Program.cs line 766 logs request details for non-protected requests.
- Impact:
  - String assembly and log I/O on each request can significantly reduce throughput under load.

## Medium Priority Findings

4. /me endpoint can trigger multiple fallback DB lookups and role recomputation.
- Evidence:
  - Program.cs lines 2946-3014 can run multiple identity queries and then role resolution.
  - Program.cs line 2405 role resolution can issue additional DB checks.
- Impact:
  - If frontend polls /me frequently, this becomes a recurring hotspot.

5. /lists loads broad graph, filters in memory, then runs an additional count query.
- Evidence:
  - Program.cs line 1388 loads lists with Creator/ListOwners/Contributors.
  - Visibility filtering is done in-memory.
  - Item counts are queried separately afterward.
- Impact:
  - Increased DB transfer and server CPU for larger datasets.

6. /posts/{itemId} renders markdown and builds HTML on every request.
- Evidence:
  - Program.cs line 1695 route logic performs per-request DB lookup(s), markdown conversion, and string assembly.
- Impact:
  - Public route traffic can accumulate avoidable CPU cost.

7. Protected file visibility path may still hit DB on cache misses/hydration.
- Evidence:
  - Program.cs lines 706 and 744 call protected-file checks and visibility logic.
  - ProtectedFiles.cs line 185 may hydrate access snapshot from DB.
- Impact:
  - First-hit/cache-miss latency can affect attachment delivery performance.

## Lower Priority Findings

8. Service locator usage in hot endpoints adds avoidable overhead.
- Evidence:
  - Program.cs lines 1393 and 1445 resolve UserManager from RequestServices instead of endpoint DI parameters.
- Impact:
  - Small per-request overhead; lower impact than DB/write/logging hotspots.

## Assumptions and Risk Notes

- Highest ROI depends on traffic profile. If frontend calls /me and /lists frequently, those are priority hotspots.
- Logging cost depends on sink and disk speed; slower sinks increase impact.
- SQLite tends to amplify cost of frequent writes and sync-over-async patterns.

## Suggested Next Step (when resumed)

- Build a ranked optimization plan with estimated impact and implementation risk for each item before changing code.
