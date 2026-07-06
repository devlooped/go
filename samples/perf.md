# Performance Analysis (detailed)

This document contains the full performance measurements for reference. The main `readme.md` only highlights the key "unchanged re-run" numbers.

## Approaches

- `go app.cs` — invoked via the RID-specific published binary (never `dotnet run` on the tool project)
- `dotnet run app.cs` — built-in file-based run (no AOT)
- `dotnet publish app.cs /p:PublishAot=... ; artifacts/app/app.exe`

## Examples

Four minimal, package-free examples live under `samples/`:

- **a) no includes** — `samples/noinc/app.cs`
- **b) #include** — `samples/include/{app.cs, lib.cs}`
- **c) #ref** — `samples/ref/{app.cs, lib.cs}`
- **d) #include + #ref** — `samples/incref/{app.cs, inclib.cs, reflib.cs}`

All declare `TargetFramework=net10.0` (plus the Experimental flag for #ref cases).

## Methodology

- Environment: Windows + .NET 11.0.100-preview.5 SDK.
- All timings via `pwsh` `Measure-Command`.
- `go` binary: always the RID-specific published exe (win-x64, --no-self-contained).
- AOT control via `/p:PublishAot=false|true`.
- Scenarios captured:
  - first-run (clean state)
  - unchanged re-runs (pristine sources)
  - run/edit/run
- Raw data is in the goal scratch directory (`perf-logs/`).

The tool provides fast re-runs for unchanged sources through its build caching.

## Unchanged re-runs (pristine sources – last observed)

**PublishAot=false**

| Example            | `dotnet run app.cs` | `go app.cs` (RID exe) | `dotnet publish ... ; app.exe` |
|--------------------|---------------------|-----------------------|--------------------------------|
| a) no includes     | 1.306s              | 0.264s                | 5.044s                         |
| b) #include        | 1.169s              | 0.198s                | 4.857s                         |
| c) #ref            | 4.605s              | 0.191s                | 6.243s                         |
| d) #include + #ref | 13.079s             | 0.227s                | 7.320s                         |

**PublishAot=true** (NativeAOT)

| Example            | `dotnet run app.cs` | `go app.cs` (RID exe) | `dotnet publish ... ; app.exe` |
|--------------------|---------------------|-----------------------|--------------------------------|
| a) no includes     | —                   | 0.227s                | 12.436s                        |
| b) #include        | —                   | 0.164s                | 11.145s                        |
| c) #ref            | —                   | 0.183s                | 32.874s                        |
| d) #include + #ref | —                   | 0.269s                | 21.888s                        |

**Observation**: `go` re-runs (both AOT modes) are now ~0.16–0.27s — just launching the already-published executable.

## First-run (clean state)

AOT=true first runs are significantly slower due to NativeAOT compilation.

| Example | `dotnet run` | `go` (AOT=false) | `publish` (AOT=false) | `go` (AOT=true) | `publish` (AOT=true) |
|---------|--------------|------------------|-----------------------|-----------------|----------------------|
| a) noinc | 6–12s       | ~20s            | ~8–13s               | ~20s           | ~17–23s             |
| b) include | ~5–11s    | ~20s            | ~7s                  | ~18s           | ~20s                |
| c) ref  | ~6–7s       | ~19s            | ~8s                  | ~20s           | ~18s                |
| d) incref | ~11s      | ~38s            | ~7.5s                | ~32s           | ~27s                |

## run / edit / run

After editing the source, the next invocation was measured (with prior artifacts left in place).

See the raw logs for exact per-cell values. The important takeaway is that once a native AOT executable has been produced by `go`, launching it remains extremely fast even after source changes (until a rebuild is triggered).

## Full raw data

All `Measure-Command` output and per-run summaries live in the private scratch directory used during measurement (typically under the goal session temp folder).

Example commands used:

```pwsh
& '...\go-tool-rid\go.exe' app.cs -- /p:PublishAot=false
& '...\go-tool-rid\go.exe' app.cs -- /p:PublishAot=true
```

---

*This file is for future reference. The primary user-facing numbers are in the root `readme.md`.*
