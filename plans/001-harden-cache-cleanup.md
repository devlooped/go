# Plan 001: Make automatic cache cleanup resilient to failed deletes and reliable to schedule

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `plans/README.md` — unless a reviewer dispatched you and told you they
> maintain the index.
>
> **Drift check (run first)**: `git diff --stat fa8800d..HEAD -- src/go/CleanupManager.cs src/go/CleanupScheduler.cs src/go/Program.cs src/Tests/CleanupManagerTests.cs`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: bug
- **Planned at**: commit `fa8800d`, 2026-07-06

## Why this matters

`go` schedules a detached background process (`go cleanup`) that deletes stale
publish-cache directories. The delete loop has no per-directory error handling:
the first directory that fails to delete (locked file — very common on Windows,
e.g. a cached app that is still running) throws, which aborts the sweep AND
skips saving `LastCleanupUtc`. Because the "last cleanup" timestamp is never
saved, `CleanupScheduler.ShouldRun()` returns true on every subsequent `go`
invocation, so a doomed cleanup process is spawned — and crashes — on every
single run, forever. Separately, the scheduling itself is a fire-and-forget
`Task.Run` that is never awaited, so for very short-lived apps the process may
exit before the cleanup process is ever spawned. This plan makes cleanup
best-effort per directory, always records the sweep, and makes scheduling
deterministic.

## Current state

Relevant files:

- `src/go/CleanupManager.cs` — the sweep logic (whole file, 26 lines). Bug is here.
- `src/go/CleanupScheduler.cs` — decides whether/how to spawn the detached `cleanup` process.
- `src/go/Program.cs` — `ExecuteAppAsync` fire-and-forgets the scheduling (line 86).
- `src/Tests/CleanupManagerTests.cs` — existing unit tests to extend.
- `src/go/Settings.cs` — `SettingsStore.Load/Save(path?)` persist `LastCleanupUtc` to `go.toml` under the temp root; already supports an explicit path parameter.

`src/go/CleanupManager.cs` as it exists today (entire file):

```csharp
namespace Devlooped;

public static class CleanupManager
{
    public const int DefaultDays = 2;

    public static int Cleanup(int days = DefaultDays)
    {
        CleanupStaleDirectories(Directory.GetTempRoot(), days);

        SettingsStore.Save(new Settings { LastCleanupUtc = DateTimeOffset.UtcNow });
        return 0;
    }

    public static void CleanupStaleDirectories(string root, int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (Directory.GetLastWriteTimeUtc(directory) >= cutoff)
                continue;

            Directory.Delete(directory, recursive: true);
        }
    }
}
```

`src/go/Program.cs:83-88` as it exists today:

```csharp
static async Task<int> ExecuteAppAsync(string publishDir, Func<Task<int>> execute)
{
    Directory.Touch(publishDir);
    _ = CleanupScheduler.TryScheduleAsync();
    return await execute();
}
```

`src/go/CleanupScheduler.cs:37-69` (`CreateCleanupProcessStartInfo`) — note the
else-if branch at lines 57-65: when the host is `dotnet` but the entry assembly
location is empty (single-file/AOT host scenario), NO assembly argument is
added, and the method still returns a start info that would run the bogus
command `dotnet cleanup`:

```csharp
static ProcessStartInfo CreateCleanupProcessStartInfo()
{
    var commandLine = Environment.GetCommandLineArgs();
    var fileName = Environment.ProcessPath ?? commandLine[0];

    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    if (IsDotnetHost(fileName) &&
        commandLine.Length >= 3 &&
        commandLine[1] == "exec" &&
        commandLine[2].EndsWith("go.dll", StringComparison.OrdinalIgnoreCase))
    {
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(commandLine[2]);
    }
    else if (IsDotnetHost(fileName))
    {
        var assembly = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrEmpty(assembly))
        {
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(assembly);
        }
    }

    startInfo.ArgumentList.Add("cleanup");
    return startInfo;
}
```

Repo conventions that apply (from `.editorconfig` and existing code):

- File-scoped namespace `namespace Devlooped;`, 4-space indent, LF line endings.
- No explicit visibility modifier when it equals the default (no `private` on
  fields/methods, no `internal` on classes) — see any file in `src/go/`.
- Test style: xUnit, method names like `CleanupStaleDirectories_deletes_directories_older_than_cutoff`,
  temp dirs created via a `CreateTempDir()` static helper. Exemplar for
  OS-conditional assertions (Windows file locks vs Unix): 
  `src/Tests/AppCleanerTests.cs`, test `Clean_deletes_stamp_when_publish_directory_delete_fails`.

## Commands you will need

| Purpose | Command | Expected on success |
|---------|---------|---------------------|
| Build | `dotnet build` | exit 0 |
| All tests | `dnx --yes retest -- --no-build` | all pass |
| Targeted tests | `dotnet test src/Tests/Tests.csproj --filter "FullyQualifiedName~Cleanup"` | all pass |
| Format check | `dotnet format whitespace --verify-no-changes -v:diag --exclude ~/.nuget` then `dotnet format style --verify-no-changes -v:diag --exclude ~/.nuget` | exit 0 |

## Scope

**In scope** (the only files you should modify):

- `src/go/CleanupManager.cs`
- `src/go/CleanupScheduler.cs`
- `src/go/Program.cs` (only the `ExecuteAppAsync` method)
- `src/Tests/CleanupManagerTests.cs`
- `src/go/help.md` — regenerated automatically by `dotnet build` (RenderHelp target); commit if it changes.

**Out of scope** (do NOT touch, even though they look related):

- `src/go/AppCleaner.cs` and `src/go/CleanupCommands.cs` — separate command surface; plan 003 changes it.
- `src/go/go.targets`, `src/go/BuildManager.cs` — caching logic, unrelated.
- The 2-day `DefaultDays` policy — do not change the constant.
- `readme.md` — documentation is plan 004.

## Git workflow

- Branch from the current branch (`add-clean-command`) or as the operator directs: `advisor/001-harden-cache-cleanup`.
- Commit style: short imperative subject, e.g. `Harden stale cache cleanup against failed deletes` (match `git log --oneline` style like "Add 'clean' command and automatic cleanup of stale caches").
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Make the sweep best-effort and always record it

In `src/go/CleanupManager.cs`:

1. Add optional `root` and `settingsPath` parameters to `Cleanup` for testability,
   mirroring the existing optional-parameter pattern of `CleanupScheduler.ShouldRun(int days, string? settingsPath)`:

```csharp
public static int Cleanup(int days = DefaultDays, string? root = null, string? settingsPath = null)
{
    CleanupStaleDirectories(root ?? Directory.GetTempRoot(), days);

    SettingsStore.Save(new Settings { LastCleanupUtc = DateTimeOffset.UtcNow }, settingsPath);
    return 0;
}
```

2. Wrap the per-directory delete in `CleanupStaleDirectories` so one failure
   does not abort the sweep (a locked directory will simply be retried on the
   next scheduled sweep):

```csharp
public static void CleanupStaleDirectories(string root, int days)
{
    var cutoff = DateTime.UtcNow.AddDays(-days);

    foreach (var directory in Directory.EnumerateDirectories(root))
    {
        if (Directory.GetLastWriteTimeUtc(directory) >= cutoff)
            continue;

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best-effort: locked/undeletable directories are retried on the next sweep.
        }
    }
}
```

Note: `CleanupCommands.Cleanup(int days)` calls `CleanupManager.Cleanup(days)` —
the new parameters are optional, so no change is needed there.

**Verify**: `dotnet build` → exit 0.

### Step 2: Don't spawn a bogus `dotnet cleanup` process

In `src/go/CleanupScheduler.cs`, change `CreateCleanupProcessStartInfo` to
return `ProcessStartInfo?` and return `null` from the else-if branch when the
entry assembly location is empty (instead of falling through and producing a
start info that runs `dotnet cleanup`). Shape:

```csharp
else if (IsDotnetHost(fileName))
{
    var assembly = Assembly.GetEntryAssembly()?.Location;
    if (string.IsNullOrEmpty(assembly))
        return null;

    startInfo.ArgumentList.Add("exec");
    startInfo.ArgumentList.Add(assembly);
}
```

Then guard the caller `StartDetached`:

```csharp
public static void StartDetached()
{
    if (CreateCleanupProcessStartInfo() is { } startInfo)
        Process.Start(startInfo);
}
```

**Verify**: `dotnet build` → exit 0.

### Step 3: Make scheduling deterministic

In `src/go/Program.cs`, change `ExecuteAppAsync` so the scheduling task is
awaited after the app finishes (it is cheap — it at most checks a timestamp
and calls `Process.Start` for a detached process — and `TryScheduleAsync`
already swallows all exceptions internally):

```csharp
static async Task<int> ExecuteAppAsync(string publishDir, Func<Task<int>> execute)
{
    Directory.Touch(publishDir);
    var cleanup = CleanupScheduler.TryScheduleAsync();
    var exit = await execute();
    await cleanup;
    return exit;
}
```

**Verify**: `dotnet build` → exit 0.

### Step 4: Add regression tests

See "Test plan" below. Add tests to `src/Tests/CleanupManagerTests.cs`.

**Verify**: `dotnet test src/Tests/Tests.csproj --filter "FullyQualifiedName~Cleanup"` → all pass, including the new tests.

## Test plan

Add to `src/Tests/CleanupManagerTests.cs`, modeled structurally after the
existing tests in that file and after the OS-conditional locking pattern in
`src/Tests/AppCleanerTests.cs` (`Clean_deletes_stamp_when_publish_directory_delete_fails`):

1. `CleanupStaleDirectories_continues_when_a_directory_cannot_be_deleted`
   - Create a temp root with two stale subdirectories (`Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddDays(-5))`).
   - In the first one, create a file and hold it open with
     `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)` (inside `using`).
   - Call `CleanupManager.CleanupStaleDirectories(root, days: 2)` — must NOT throw.
   - On Windows: assert the locked directory still exists and the other stale directory was deleted.
   - On non-Windows (open handles do not block deletion): assert both directories were deleted.
2. `Cleanup_saves_settings_even_when_a_delete_fails`
   - Same locked-directory setup (Windows-meaningful; still valid on Unix).
   - Call `CleanupManager.Cleanup(days: 2, root: root, settingsPath: Path.Combine(root2, "go.toml"))`
     where `root2` is a separate temp dir (so the settings file isn't swept).
   - Assert return value is 0 and `SettingsStore.Load(settingsPath).LastCleanupUtc` is not null.

Verification: `dotnet test src/Tests/Tests.csproj --filter "FullyQualifiedName~Cleanup"` → all pass (existing 5 + 2 new).

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `dotnet build` exits 0
- [ ] `dnx --yes retest -- --no-build` exits 0; the 2 new tests exist and pass
- [ ] `src/go/CleanupManager.cs` contains a `try`/`catch` around `Directory.Delete` inside the sweep loop
- [ ] `src/go/Program.cs` no longer contains the line `_ = CleanupScheduler.TryScheduleAsync();`
- [ ] `dotnet format whitespace --verify-no-changes -v:diag --exclude ~/.nuget` and `dotnet format style --verify-no-changes -v:diag --exclude ~/.nuget` both exit 0
- [ ] `git status` shows no modified files outside the in-scope list
- [ ] `plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- The code at the locations in "Current state" doesn't match the excerpts.
- `CleanupCommands.Cleanup` stops compiling after Step 1 (signature drift).
- The Windows-locked-directory test fails on Windows twice after a reasonable
  fix attempt (locking semantics may differ from the assumption; report what
  you observed instead of loosening the assertion).
- Any test outside `Cleanup*`/`AppCleaner*` starts failing.

## Maintenance notes

- Plan 003 (`go clean --all`) builds on the new `Cleanup(days, root, settingsPath)`
  overload and on the sweep being crash-safe — keep the optional parameters.
- Reviewers should scrutinize: the `catch` in the sweep is intentionally
  empty (background best-effort process, nothing to report to); the awaited
  `cleanup` task in `ExecuteAppAsync` must never be able to throw
  (`TryScheduleAsync` wraps everything in try/catch — verify that remains true).
- Deferred (out of scope here): a narrow race where the detached cleanup can
  delete a 2+day-old cache directory while another `go` process is actively
  re-publishing into it. Overwriting the stamp file does not update the
  directory mtime. If this ever bites, touch the publish directory at the
  start of the rebuild path (in `RunAsync`/`DevAsync`), not only in `ExecuteAppAsync`.
