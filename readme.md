![Icon](https://raw.githubusercontent.com/devlooped/go/main/assets/img/icon.png) go#
============

[![Version](https://img.shields.io/nuget/vpre/go.svg?color=royalblue)](https://www.nuget.org/packages/go)
[![Downloads](https://img.shields.io/nuget/dt/go.svg?color=darkmagenta)](https://www.nuget.org/packages/go)
[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](https://github.com/devlooped/oss/blob/main/osmfeula.txt)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/devlooped/oss/blob/main/license.txt)

<!-- #content -->

## What is `go#`?

`go#` (go sharp) lets you run `.cs` files directly, like scripts, while still getting the full power of the .NET SDK (dependencies, compilation, AOT, etc.).

It shines for:
- Quick one-off tools and prototypes
- File-based apps without a `.csproj`
- Fast iteration with smart caching (subsequent runs are near-instant when nothing changed)
- Easy sharing of small utilities (just a `.cs` file **or** a remote ref like `owner/repo[@ref][:path]`)

`go#` optimizes the underlying `dotnet publish` and `dotnet run` commands for file-based apps, with smart up-to-date checks 
of every C# file used to build the app (including `#include` and `#ref` directives, transitively), 
making it optimal for quick iteration and agentic tools authoring and consumption.


## Usage

```console
# Run a file
dnx go -- app.cs

# Pass arguments to your app
dnx go -- app.cs arg1 arg2

# Re-run a previous app from interactive history (no path/ref required)
dnx go
```

The default mode publishes the app with native AOT and then runs the resulting executable, 
with smart up-to-date checks of every C# file used to build the app (including 
`#include` and `#ref` directives, transitively).

### Run history (MRU)

Every successful `go` / `go dev` invocation records the entry point (local full path or
remote ref) in a shared history file next to the cache root (`dotnet/go/go.toml`).

With at least one history entry, running with **no arguments** in an interactive
terminal opens a searchable picker (ordered by use count then recency). After you
pick an entry you can optionally type app arguments (quoted groups supported).
With an empty history, or when stdin is redirected / non-interactive (CI, pipes),
`dnx go` with no args shows help instead of erroring.

Local paths that no longer exist are dropped from the picker; remote refs stay listed
even when their download bundle is gone (the next run re-downloads as usual).

Gists are labeled as `owner/shortsha:file` (first seven characters of the gist id),
for example `kzu/0ac826d:run.cs`, instead of the full host URL.

```console
# Interactive: pick a previous run, then optional args
dnx go

# Same as always — also bumps that entry in history
dnx go -- app.cs
dnx go -- kzu/sandbox
```
Native AOT needs a platform C/C++ linker (VC++ build tools on Windows, `build-essential` on
Ubuntu, Xcode Command Line Tools on macOS). Verify with:

```console
dnx go -- check
```

On failure, the command prints the recommended install command for your OS (for example
`dnx vs -- install --passive --sku:build` on Windows).

Use `--r2r` when your app needs more dynamic .NET features (reflection, dynamic loading, etc.) 
that native AOT does not support, while still keeping most publish optimizations:

```console
dnx go -- app.cs --r2r

# Pass arguments to your app
dnx go -- app.cs --r2r arg1 arg2
```

This publishes with `/p:PublishAot=false` and `/p:PublishReadyToRun=true`. 
An equivalent `--aot` switch is not needed since native AOT is the default for file-based apps.

A dev mode is also available for faster iteration, which skips the publish step 
and runs the app directly from the build output without the optimizations 
applied by dotnet to published executables (i.e. AOT, RID-specific optimizations):

```console
dnx go -- dev app.cs

# Pass arguments to your app
dnx go -- dev app.cs arg1 arg2
```

## Remote references

Instead of a local `.cs` file, you can pass a remote reference. The tool will download
the content (when needed) and treat the resulting local file as the entry point:

```console
# Run from a public repo (defaults to github.com, main + program.cs or first .cs)
dnx go -- kzu/sandbox

# Specific branch/tag and file
dnx go -- kzu/sandbox@v1.2.3:src/hello.cs

# Full host (GitHub, Gist, GitLab, Azure DevOps)
dnx go -- github.com/kzu/sandbox@main:hello.cs
dnx go -- gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381
# Multi-file gist: pick a specific entry file with :path (same as repos)
dnx go -- gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381:run.cs
dnx go -- gitlab.com/kzu/runcs/-/blob/main/program.cs
```

Without `:path`, a gist (or repo) defaults to `program.cs` when present, otherwise
the first top-level `.cs` file. Use `:filename.cs` when a multi-file gist should
run a different entry point.

The first argument is resolved by first checking if it is a local file (`File.Exists`).
If not, it falls back to parsing it as a remote ref (`owner/repo[@ref][:path]`).

Downloaded content is cached under the `dotnet/go` directory (same root as local apps)
and participates in the normal up-to-date checks. Remote refs are always revalidated
by sending a conditional request (using ETag when available) to the source. A 304
Not Modified response means the local copy is used as-is.

To force a fresh download for a remote ref, clean its bundle first:

```console
# Clean the downloaded bundle for a remote ref (forces full download on next run)
dnx go -- clean kzu/sandbox

# Works for refs with @ref or :path too (the bundle for the ref is deleted entirely)
dnx go -- clean kzu/sandbox@main:program.cs
```

Behavior follows the chosen command:

* Default command: downloads (if needed) then `dotnet publish` + execute (AOT by default).
* `dev` command: downloads (if needed) then `dotnet run` for fast iteration.

Trailing arguments are passed to the app, the same as with local files.

## Cache and cleaning

`go#` caches build and publish outputs per entry-point file under the
user's temp area, which is what makes unchanged re-runs near-instant.

```console
# Delete the cached artifacts for a single app (next run rebuilds)
dnx go -- clean app.cs

# Delete the downloaded bundle for a remote ref (next run re-downloads; :path ignored)
dnx go -- clean owner/repo[@ref][:path]

# Delete the cached artifacts for all apps
dnx go -- clean --all
```

`clean` only removes build/publish/download artifacts. Run history is left intact
so the MRU picker still lists those apps (a later run simply rebuilds or re-downloads).

To drop history as well, use `remove`:

```console
# Clean artifacts and remove this entry from MRU history
dnx go -- remove app.cs
dnx go -- remove owner/repo[@ref][:path]

# Wipe all cached apps and clear the entire MRU history
dnx go -- remove --all
```

Unused download locations and published binaries are periodically cleaned up
in a detached background process. Apps you run regularly are never affected.
Automatic cleanup updates `lastCleanupUtc` in `go.toml` but does not clear history.
## Agent skill

`go#` ships a bundled [agent skill](skills/go-sharp/SKILL.md) that teaches coding
agents how to author and run file-based C# apps with `dnx go`. Install it for
global use or into the current repo:

```console
# Install to ~/.agents/skills/go-sharp/SKILL.md (prompts for confirmation)
dnx go -- skill

# Install for the current project under .agents/skills/go-sharp/SKILL.md
dnx go -- skill .

# Skip the confirmation prompt
dnx go -- skill -y
dnx go -- skill . --yes

# Remove a previously installed skill (same path rules as install)
dnx go -- skill remove
dnx go -- skill remove .
dnx go -- skill remove -y
```

With no directory, the skill is written under the user home directory. Pass a
base directory (commonly `.`) to install under that location instead. Either
form overwrites an existing install.

## Performance

The main advantage of `go#` is **fast unchanged re-runs**. 
The two core scenarios for `go#` file-based apps are:
* While tweaking 👉 `dnx go -- dev app.cs` (optimized `dotnet run app.cs`)
* When stable 👉 `dnx go -- app.cs` (optimized `dotnet publish app.cs; app[.exe]`)

The numbers below showcase both scenarios, comparing `go#` to `dotnet run` and 
`dotnet publish` for a file-based app with different combinations of `#include` and `#ref` directives.

<!-- include ./artifacts/results/Benchmarks-report-github.md -->
```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
AMD Ryzen AI 9 HX 370 w/ Radeon 890M 2.00GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 11.0.100-preview.5.26302.115
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4
  Job-TASYDQ : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=3  LaunchCount=1  
UnrollFactor=1  WarmupCount=1  

```
| Method           | Sample          | Mean       | Error       | StdDev   |
|----------------- |---------------- |-----------:|------------:|---------:|
| **&#39;dnx go&#39;**         | **#include**        |   **483.2 ms** |    **85.70 ms** |  **4.70 ms** |
| &#39;dotnet publish&#39; | #include        | 3,302.0 ms | 1,003.60 ms | 55.01 ms |
| &#39;dnx go dev&#39;     | #include        |   500.1 ms |   250.72 ms | 13.74 ms |
| &#39;dotnet run&#39;     | #include        |   475.6 ms |   100.88 ms |  5.53 ms |
| **&#39;dnx go&#39;**         | **#include + #ref** |   **481.4 ms** |   **236.79 ms** | **12.98 ms** |
| &#39;dotnet publish&#39; | #include + #ref | 3,474.3 ms |   391.66 ms | 21.47 ms |
| &#39;dnx go dev&#39;     | #include + #ref |   498.3 ms |   290.97 ms | 15.95 ms |
| &#39;dotnet run&#39;     | #include + #ref | 1,426.7 ms |   318.56 ms | 17.46 ms |
| **&#39;dnx go&#39;**         | **#ref**            |   **487.1 ms** |   **140.05 ms** |  **7.68 ms** |
| &#39;dotnet publish&#39; | #ref            | 3,509.4 ms |   616.31 ms | 33.78 ms |
| &#39;dnx go dev&#39;     | #ref            |   505.2 ms |   264.59 ms | 14.50 ms |
| &#39;dotnet run&#39;     | #ref            | 1,413.9 ms |   267.93 ms | 14.69 ms |
| **&#39;dnx go&#39;**         | **minimal**         |   **480.8 ms** |   **486.66 ms** | **26.68 ms** |
| &#39;dotnet publish&#39; | minimal         | 3,237.4 ms |   567.55 ms | 31.11 ms |
| &#39;dnx go dev&#39;     | minimal         |   488.4 ms |   209.11 ms | 11.46 ms |
| &#39;dotnet run&#39;     | minimal         |   482.5 ms |   507.46 ms | 27.82 ms |

<!-- ./artifacts/results/Benchmarks-report-github.md -->

<!-- #content -->
---

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
## Open Source Maintenance Fee

To ensure the long-term sustainability of this project, users of this package who generate 
revenue must pay an [Open Source Maintenance Fee](https://opensourcemaintenancefee.org). 
While the source code is freely available under the terms of the [License](license.txt), 
this package and other aspects of the project require [adherence to the Maintenance Fee](osmfeula.txt).

To pay the Maintenance Fee, [become a Sponsor](https://github.com/sponsors/devlooped) at the proper 
OSMF tier. A single fee covers all of [Devlooped packages](https://www.nuget.org/profiles/Devlooped).

<!-- https://github.com/devlooped/.github/raw/main/osmf.md -->
---
<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
# Sponsors 

<!-- sponsors.md -->
[![Clarius Org](https://avatars.githubusercontent.com/u/71888636?v=4&s=39 "Clarius Org")](https://github.com/clarius)
[![MFB Technologies, Inc.](https://avatars.githubusercontent.com/u/87181630?v=4&s=39 "MFB Technologies, Inc.")](https://github.com/MFB-Technologies-Inc)
[![SandRock](https://avatars.githubusercontent.com/u/321868?u=99e50a714276c43ae820632f1da88cb71632ec97&v=4&s=39 "SandRock")](https://github.com/sandrock)
[![DRIVE.NET, Inc.](https://avatars.githubusercontent.com/u/15047123?v=4&s=39 "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://avatars.githubusercontent.com/u/16598898?u=64416b80caf7092a885f60bb31612270bffc9598&v=4&s=39 "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://avatars.githubusercontent.com/u/127185?u=7f50babfc888675e37feb80851a4e9708f573386&v=4&s=39 "Thomas Bolon")](https://github.com/tbolon)
[![Kori Francis](https://avatars.githubusercontent.com/u/67574?u=3991fb983e1c399edf39aebc00a9f9cd425703bd&v=4&s=39 "Kori Francis")](https://github.com/kfrancis)
[![Reuben Swartz](https://avatars.githubusercontent.com/u/724704?u=2076fe336f9f6ad678009f1595cbea434b0c5a41&v=4&s=39 "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://avatars.githubusercontent.com/u/480334?v=4&s=39 "Jacob Foshee")](https://github.com/jfoshee)
[![](https://avatars.githubusercontent.com/u/33566379?u=bf62e2b46435a267fa246a64537870fd2449410f&v=4&s=39 "")](https://github.com/Mrxx99)
[![Eric Johnson](https://avatars.githubusercontent.com/u/26369281?u=41b560c2bc493149b32d384b960e0948c78767ab&v=4&s=39 "Eric Johnson")](https://github.com/eajhnsn1)
[![Jonathan ](https://avatars.githubusercontent.com/u/5510103?u=98dcfbef3f32de629d30f1f418a095bf09e14891&v=4&s=39 "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Ken Bonny](https://avatars.githubusercontent.com/u/6417376?u=569af445b6f387917029ffb5129e9cf9f6f68421&v=4&s=39 "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://avatars.githubusercontent.com/u/122666?v=4&s=39 "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://avatars.githubusercontent.com/u/5989304?v=4&s=39 "agileworks-eu")](https://github.com/agileworks-eu)
[![Zheyu Shen](https://avatars.githubusercontent.com/u/4067473?v=4&s=39 "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://avatars.githubusercontent.com/u/87844133?v=4&s=39 "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://avatars.githubusercontent.com/u/16239022?v=4&s=39 "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://avatars.githubusercontent.com/u/68428092?v=4&s=39 "4OTC")](https://github.com/4OTC)
[![domischell](https://avatars.githubusercontent.com/u/66068846?u=0a5c5e2e7d90f15ea657bc660f175605935c5bea&v=4&s=39 "domischell")](https://github.com/DominicSchell)
[![Adrian Alonso](https://avatars.githubusercontent.com/u/2027083?u=129cf516d99f5cb2fd0f4a0787a069f3446b7522&v=4&s=39 "Adrian Alonso")](https://github.com/adalon)
[![torutek](https://avatars.githubusercontent.com/u/33917059?v=4&s=39 "torutek")](https://github.com/torutek)
[![Ryan McCaffery](https://avatars.githubusercontent.com/u/16667079?u=c0daa64bb5c1b572130e05ae2b6f609ecc912d4d&v=4&s=39 "Ryan McCaffery")](https://github.com/mccaffers)
[![Seika Logiciel](https://avatars.githubusercontent.com/u/2564602?v=4&s=39 "Seika Logiciel")](https://github.com/SeikaLogiciel)
[![Andrew Grant](https://avatars.githubusercontent.com/devlooped-user?s=39 "Andrew Grant")](https://github.com/wizardness)
[![eska-gmbh](https://avatars.githubusercontent.com/devlooped-team?s=39 "eska-gmbh")](https://github.com/eska-gmbh)
[![Geodata AS](https://avatars.githubusercontent.com/u/5946299?v=4&s=39 "Geodata AS")](https://github.com/geodata-no)


<!-- sponsors.md -->
[![Sponsor this project](https://avatars.githubusercontent.com/devlooped-sponsor?s=118 "Sponsor this project")](https://github.com/sponsors/devlooped)

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://github.com/devlooped/sponsors/raw/main/footer.md -->
