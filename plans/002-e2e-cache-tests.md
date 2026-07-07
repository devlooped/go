# Plan 002: Add end-to-end tests for the publish→stamp→cache-hit loop

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `plans/README.md` — unless a reviewer dispatched you and told you they
> maintain the index.
>
> **Drift check (run first)**: `git diff --stat fa8800d..HEAD -- src/go/go.targets src/go/Program.cs src/go/BuildManager.cs src/Tests`
> If any in-scope or excerpted file changed since this plan was written,
> compare the "Current state" excerpts against the live code before
> proceeding; on a mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: LOW
- **Depends on**: none (001 recommended first but not required)
- **Category**: tests
- **Planned at**: commit `fa8800d`, 2026-07-06

## Why this matters

`go`'s entire value proposition is stamp-based incremental caching: the first
run builds/publishes a C# file-based app and records inputs + artifact paths
in a `go.stamp` file (written partly by the tool, partly by MSBuild targets in
`src/go/go.targets`); subsequent runs skip the build entirely when nothing
changed. Today only the pure logic (`BuildState.TryRead`, `BuildManager.IsUpToDate`,
`GoArgs`) is unit-tested. The integration — `go.targets` executing inside a real
`dotnet run`/`dotnet publish`, the stamp round-trip, and the cache-hit decision —
has **zero automated coverage**. A regression in `go.targets` (e.g. the private
SDK property `$(_NativeExecutableExtension)` it relies on being renamed in a
future SDK) would not fail anything; it would just silently make every run pay
the full multi-second publish. This plan adds E2E tests that run the actual
tool against a real file-based app and assert the cache-hit behavior.

## Current state

How the tool works (all facts verified at commit `fa8800d`):

- `src/go/Program.cs` — entry point. Commands: default (`go app.cs`, publish +
  run, native AOT by default, `--r2r` for ReadyToRun), `dev` (`go dev app.cs`,
  `dotnet run` + cached re-runs), `clean`, hidden `cleanup`.
- The cache lives in `<temp-root>/<filename>-<sha256-of-uppercased-fullpath>/`
  where `<temp-root>` is `Path.GetTempPath()/dotnet/go` on Windows and
  `Environment.SpecialFolder.LocalApplicationData/dotnet/go` on Unix
  (see `src/go/DirectoryExtensions.cs:29-48`). The stamp file is `go.stamp`
  inside that directory (`src/go/Program.cs:102`).
- On a build, the tool writes the initial stamp content, then MSBuild targets
  (`src/go/go.targets`, injected via `CustomAfterMicrosoftCSharpTargets`)
  append `input=`, `bin=`, and (publish only) `app=` lines to the stamp.
- On a cache hit (`BuildManager.IsUpToDate` true), the stamp is **not
  rewritten** — the tool directly executes the recorded artifact. On a cache
  miss the stamp is truncated and rebuilt. This gives a machine-checkable
  signal: the stamp's content/last-write-time changes on rebuild and stays
  identical on a cache hit.
- `src/Tests/Tests.csproj` has `<ProjectReference Include="..\go\go.csproj" />`,
  and `go.csproj` marks `go.targets` as `Content` with
  `CopyToOutputDirectory="PreserveNewest"` — so the test output directory
  contains both `go.dll` and `go.targets`. The tool can be invoked in-process
  output as `dotnet <testbin>/go.dll <args>`.
- `Devlooped.DotnetMuxer.Path` (public, `src/go/DotnetMuxer.cs`) resolves the
  `dotnet` executable; usable from tests.
- Native AOT publish requires a native toolchain and is slow; **do not use the
  default AOT mode in tests**. Use `go dev` (no publish) and `go --r2r`
  (ReadyToRun publish — works on all CI images without extra prerequisites).
- CI (`.github/workflows/build.yml`) runs `dotnet build` then
  `dnx --yes retest -- --no-build` on `ubuntu-latest`; the pinned SDK channel is
  `10.x` (`.github/dotnet.json`). Test apps must target `net10.0`.

Excerpt — `src/go/Program.cs:65-81` (`dev` command), the up-to-date fast path
and the stamp truncation on rebuild:

```csharp
static async Task<int> DevAsync([Argument] string input, [Argument] params string[] extraArgs)
{
    var context = Prepare(input, extraArgs);
    if (context is null)
        return 1;

    var (dotnet, cs, publishDir, stamp, targets, _, dotnetArgs, appArgs) = context.Value;

    if (BuildState.TryRead(stamp, out var state) &&
        state.Bin is not null &&
        BuildManager.IsUpToDate(state, state.Bin))
        return await ExecuteAppAsync(publishDir, () => ProcessRunner.DotnetExecAsync(dotnet, state.Bin, appArgs));

    File.WriteAllText(stamp, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    return await ExecuteAppAsync(publishDir, () => ProcessRunner.DotnetRunAsync(dotnet, cs, stamp, targets, dotnetArgs, appArgs));
}
```

Excerpt — an existing minimal file-based app (`samples/minimal/app.cs`) to
model the fixture after:

```csharp
#!/usr/bin/env dotnet
#:property TargetFramework=net10.0

Console.WriteLine("hello from go");
```

Repo test conventions: xUnit, `namespace Tests;`, snake-style method names
(`Method_does_x_when_y`), temp dirs `Path.Combine(Path.GetTempPath(), "go-tests-" + Guid.NewGuid().ToString("N"))`
via a `CreateTempDir()` helper. Exemplar: `src/Tests/GoBuildCacheTests.cs`.

## Commands you will need

| Purpose | Command | Expected on success |
|---------|---------|---------------------|
| Build | `dotnet build` | exit 0 |
| All tests | `dnx --yes retest -- --no-build` | all pass |
| Targeted tests | `dotnet test src/Tests/Tests.csproj --filter "FullyQualifiedName~EndToEnd"` | all pass |
| Format check | `dotnet format whitespace --verify-no-changes -v:diag --exclude ~/.nuget` then `dotnet format style --verify-no-changes -v:diag --exclude ~/.nuget` | exit 0 |

## Scope

**In scope** (the only files you should modify/create):

- `src/Tests/EndToEndTests.cs` (create)

**Out of scope** (do NOT touch, even though they look related):

- `src/go/**` — no product changes in this plan. If the tests reveal a product
  bug, that is a STOP condition (report it), not a license to fix it here.
- `samples/**` — committed samples carry checked-in `.stamp` files with
  machine-specific paths; do NOT run the tool against them from tests. Create
  fresh fixtures in a temp directory instead.
- `.github/workflows/**` — CI already runs all tests; no changes needed.
- `src/Tests/Tests.csproj` — no new packages are needed; plain xUnit suffices.

## Git workflow

- Branch: `advisor/002-e2e-cache-tests`.
- Commit style: short imperative subject, e.g. `Add end-to-end tests for stamp-based cache hits` (match `git log --oneline`).
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Create the E2E test scaffold

Create `src/Tests/EndToEndTests.cs` with `namespace Tests;` and a class
`EndToEndTests` containing these private helpers:

1. `static string CreateApp(string marker)` — creates a unique temp dir
   (existing `go-tests-` + GUID convention), writes `app.cs` into it with:

   ```csharp
   #:property TargetFramework=net10.0

   Console.WriteLine("MARKER");
   ```

   (with `MARKER` replaced by the `marker` argument) and returns the full path
   to `app.cs`. A unique marker per test (e.g. `"e2e-" + Guid.NewGuid().ToString("N")`)
   makes output assertions unambiguous.

2. `static string GetTempRoot()` — mirrors `src/go/DirectoryExtensions.cs:40-48`
   (internal, so re-implement the 4 lines):

   ```csharp
   var directory = OperatingSystem.IsWindows()
       ? Path.GetTempPath()
       : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   return Path.Join(directory, "dotnet", "go");
   ```

3. `static string FindStamp(string appFile)` — the publish dir name is
   `<filenameWithoutExtension>-<hash>`; rather than reimplementing the hash,
   glob: `Directory.GetDirectories(GetTempRoot(), Path.GetFileNameWithoutExtension(appFile) + "-*")`,
   pick the single directory whose `go.stamp` contains a line
   `input=<appFile with '\' replaced by '/'>` (guards against collisions with
   other `app-*` cache dirs on the machine), and return the `go.stamp` path.
   Because each fixture uses a fresh GUID temp path, exactly one match must exist.

4. `static (int ExitCode, string Output) RunGo(params string[] args)` — starts
   `Devlooped.DotnetMuxer.Path!.FullName` with `ArgumentList` =
   `[Path.Combine(AppContext.BaseDirectory, "go.dll"), ..args]`,
   `RedirectStandardOutput = true`, `RedirectStandardError = true`,
   `UseShellExecute = false`. Read both streams concurrently
   (`process.StandardOutput.ReadToEndAsync()` + `StandardError.ReadToEndAsync()`,
   then `process.WaitForExit()`, then await both) to avoid pipe-buffer
   deadlocks. Return exit code and combined output. Set environment variable
   `NO_COLOR=true` on the start info for stable output.

5. `static void CleanApp(string appFile)` — best-effort teardown: call
   `RunGo("clean", appFile)` inside try/catch, and delete the fixture temp dir.

**Verify**: `dotnet build` → exit 0.

### Step 2: Dev-mode cache-hit test

Add test `Dev_second_run_hits_cache_and_edit_invalidates`:

1. `var app = CreateApp(marker);`
2. First run: `RunGo("dev", app)` → assert exit 0 and output contains `marker`.
3. `var stamp = FindStamp(app);` → assert `File.Exists(stamp)`; record
   `File.ReadAllText(stamp)` and `File.GetLastWriteTimeUtc(stamp)`.
4. Second run: `RunGo("dev", app)` → assert exit 0, output contains `marker`,
   AND the stamp file's content and last-write-time are **unchanged** (cache
   hit does not rewrite the stamp — see `DevAsync` excerpt above).
5. Edit: rewrite `app.cs` with a new marker `marker2` (this bumps the source
   mtime and changes output). If the file system has coarse mtime resolution,
   also call `File.SetLastWriteTimeUtc(app, DateTime.UtcNow.AddSeconds(2))`.
6. Third run: `RunGo("dev", app)` → assert exit 0, output contains `marker2`
   and NOT `marker`, and the stamp content changed from the recording in step 3.
7. Teardown in `finally`: `CleanApp(app)`.

**Verify**: `dotnet test src/Tests/Tests.csproj --filter "FullyQualifiedName~EndToEnd"` → this test passes. Expect the first run to take tens of seconds (real `dotnet run`); that is normal.

### Step 3: R2R publish cache-hit test

Add test `Publish_r2r_second_run_hits_cache`:

1. `var app = CreateApp(marker);`
2. First run: `RunGo(app, "--r2r")` → assert exit 0, output contains `marker`.
3. `var stamp = FindStamp(app);` → assert stamp content contains a line
   starting with `app=` (the published executable recorded by the
   `AppendGoApp` target in `src/go/go.targets`) and a `mode=r2r` line.
4. Record stamp content + last-write-time. Second run: `RunGo(app, "--r2r")` →
   assert exit 0, output contains `marker`, stamp unchanged (content and
   last-write-time).
5. Teardown in `finally`: `CleanApp(app)`.

**Verify**: `dotnet test src/Tests/Tests.csproj --filter "FullyQualifiedName~EndToEnd"` → both E2E tests pass.

### Step 4: Full suite + format

**Verify**: `dnx --yes retest -- --no-build` → all tests pass.
**Verify**: both `dotnet format` check commands → exit 0.

## Test plan

The tests ARE the deliverable; cases covered:

- dev mode: first-run build, cache hit on unchanged re-run, invalidation on
  source edit (the core regression this plan guards).
- publish mode (`--r2r`): first-run publish records `app=` + `mode=r2r` in the
  stamp via `go.targets`; unchanged re-run is a cache hit.
- Structural pattern: `src/Tests/GoBuildCacheTests.cs` (temp-dir helpers,
  assertion style).

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `src/Tests/EndToEndTests.cs` exists with the two tests above
- [ ] `dotnet build` exits 0
- [ ] `dnx --yes retest -- --no-build` exits 0 (all tests, old and new)
- [ ] Both `dotnet format` check commands exit 0
- [ ] `git status` shows no modified files outside the in-scope list
- [ ] `plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- `go.dll` or `go.targets` is not present in the test output directory
  (`AppContext.BaseDirectory`) after `dotnet build` — the ProjectReference/
  Content assumptions have drifted.
- The stamp file IS rewritten on an unchanged second run (content or
  last-write-time differs) — that is a product bug or a changed contract;
  report it rather than weakening the assertion.
- `--r2r` publish fails in your environment for toolchain reasons — report the
  exact error; do not switch the test to AOT or delete it.
- The second run's output contains MSBuild build evidence AND the stamp
  changed — cache misses on unchanged input; likely a real regression; report.
- Any pre-existing test starts failing.

## Maintenance notes

- These tests execute real `dotnet run`/`dotnet publish`; they add roughly
  30–90 s to the suite. If suite time becomes a problem, gate them behind an
  xUnit trait or a CI-only fact attribute — do not delete them.
- If the SDK's file-based-app cache layout or `go.targets` contract changes
  (e.g. `_NativeExecutableExtension` renamed upstream), these are the tests
  that will catch it — expect them, not the unit tests, to fail first.
- Follow-up deferred: an E2E case for `#:include`/`#:ref` invalidation
  (adding a file matched by an include glob does NOT currently invalidate the
  cache — known limitation, see audit finding 4 in `plans/README.md`).
