# Improvement plans

Written by an advisor session on 2026-07-06 against commit `fa8800d`
(branch `add-clean-command`). Audit level: standard, all nine categories,
whole repo (~500 LOC — no subagents needed). Plans are self-contained: an
executor needs no context beyond the plan file itself.

## Execution order and status

| # | Plan | Priority | Effort | Depends on | Status |
|---|------|----------|--------|------------|--------|
| 001 | [Harden background cache cleanup](001-harden-cache-cleanup.md) | P1 | S | — | DONE |
| 002 | [End-to-end cache behavior tests](002-e2e-cache-tests.md) | P1 | M | — | DONE |
| 003 | [`go clean --all`](003-clean-all.md) | P2 | S | 001 | DONE |
| 004 | [Readme: cache and cleaning docs](004-readme-cache-docs.md) | P2 | S | 003 | TODO |

Recommended order: **001 → 002 → 003 → 004**.

- 001 and 002 are independent of each other; either can go first, but 001 is
  the bug fix and should land before the next release.
- 003 requires the `Cleanup(days, root, settingsPath)` overload and
  crash-safe sweep introduced by 001.
- 004 documents the CLI surface finalized by 003 (it degrades gracefully if
  003 is rejected — see its Depends-on note).

Status values: `TODO`, `IN PROGRESS`, `DONE`, `BLOCKED (reason)`, `STALE`.
Executors: update your row when you start and when you finish.

## Findings not selected for planning

Recorded so future audits don't rediscover them. Evidence verified at
`fa8800d`.

| Finding | Category | Why not planned |
|---------|----------|-----------------|
| `go.targets:25` depends on `$(_NativeExecutableExtension)`, a private SDK property — a future SDK rename silently breaks AOT stamp `bin=` entries | tech debt | Real but low urgency; revisit when bumping SDK major. Mitigation would be defaulting the extension per-OS in go.targets. |
| Stamp inputs only track `Compile` items — `#:include`-ed content files, globbed additions, and `Directory.Build.props` edits don't invalidate the cache | correctness | Needs design (what item groups to hash) — flagged as follow-up in plan 002, where the E2E harness makes it testable. |
| CI builds/tests on ubuntu only; Windows-specific temp-root and process-spawn paths are never exercised in CI | DX/tooling | Cheap (`os-matrix.json` is already supported by the workflow) but maintainer's call on CI minutes. One-line change if wanted. |
| No AGENTS.md documenting the stamp format, go.targets injection contract, and cleanup architecture | docs | Worth doing once the cleanup feature settles (after 001/003); repo instructions already mandate it. |
| Tool startup (~480 ms JIT) eats most of the cache win vs `dotnet run` for trivial apps — AOT-publishing the tool itself (direction A) would fix it | direction/perf | Larger packaging effort (per-RID native tool packages); user deferred. |
| Run-from-URL (`go https://…/app.cs`) (direction C) | direction | Deferred by user; security design needed (trust prompt, pinning). |

## Findings considered and rejected during vetting

Do not re-report these:

- **nuget.config trusted signers warnings** — matches the devlooped org
  standard config; by design.
- **Test temp-dir leakage on assert failure** — xUnit fixtures already clean
  the happy path; failure-path leakage into `%TEMP%` is acceptable noise, and
  the auto-cleanup feature itself reaps it.
- **Benchmark runner stdout/stderr read order (theoretical deadlock)** —
  benchmarks are dev-only tooling, buffers involved are tiny.
- **`help.md` mutated by build** — intentional: the `RenderHelp` target keeps
  help text in sync; the file is committed on purpose.
- **Duplicated helper code across test classes** — trivial, tests are small;
  consolidation would churn more than it saves. Plan 002 keeps its helpers
  local for self-containment.
- **`--r2r` ↔ default (AOT) mode switch thrashes the cache** — by design:
  `mode=` is part of the stamp, and mixed-mode caching would double disk use.

## Not audited

- `bin/`, `obj/`, and gitignored local dirs (`mcps/`, `terminals/`).
- Infra files synced from `devlooped/oss` (workflows other than
  build/publish, SponsorLink footers) — owned upstream.
- NuGet dependency vulnerability scan (no lockfiles; `dotnet list package
  --vulnerable` requires restore, which mutates the tree — out of advisor
  scope).
