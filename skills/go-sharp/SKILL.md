---
name: go-sharp
description: "Run and iterate on file-based C# apps with go# (`dnx go`). Use for one-file or multi-file C# apps (`#:include`/`#:exclude`/`#:ref`), NuGet packages, remote refs, utilities, automation, and agent tooling. Prefer `dnx go -- dev app.cs` while tweaking and `dnx go -- app.cs` exclusively once stable."
license: MIT
---

# go# (file-based C# apps)

`go#` runs `.cs` files like scripts with the full .NET SDK (packages, multi-file composition, AOT publish). It optimizes `dotnet publish` / `dotnet run` with smart up-to-date checks across every C# input (including `#:include` and `#:ref`, transitively).

> go# == dnx go

**Always invoke via `dnx go`.** Do not use shebangs or direct `./app.cs` execution.

| Phase | Command | Behavior |
|-------|---------|----------|
| **Tweaking / evolving** | `dnx go -- dev app.cs` | Optimized `dotnet run` + up-to-date checks; skip publish/AOT |
| **Stable (exclusive)** | `dnx go -- app.cs` | Optimized `dotnet publish` (native AOT by default) + run the binary |
| **Prerequisites** | `dnx go -- check` | Verifies the native C/C++ toolchain needed for AOT publishes |

Once the script is stable, use **`dnx go -- app.cs` only**—not `dev`, not plain `dotnet app.cs`.

## When to Use

- Testing a C# concept, API, or language feature with a file-based app
- Building a small utility from one entry point and a few helper `.cs` files
- Scripts or tools that will be run repeatedly (especially by agents)
- Running or discovering shared utilities via remote refs (with user consent)

## When Not to Use

- Language-agnostic throwaway scripts better suited to shell/Python/PowerShell
- Full projects, solution integration, or work that belongs inside an existing `.csproj`
- The user is extending an existing .NET solution in place

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| C# code or intent | Yes | The code to run, or a description of what the app should do |
| Local path or remote ref | When running | Entry-point `.cs` path, or remote ref after explicit user trust |

## Workflow

### Step 1: Prerequisites

Run `dotnet --version`. File-based apps need **.NET 10+**. `#:include`, `#:exclude`, and transitive directive processing need **SDK 10.0.300+** (10.0.100/10.0.200 can run single-file apps but not those multi-file directives).

`dnx` ships with modern .NET SDKs. The first `dnx go` invocation pulls the `go` tool package automatically:

```bash
dnx go -- --help
```

#### Native toolchain (AOT publishes)

The **stable** path (`dnx go -- app.cs`) publishes with **native AOT** by default and needs a platform C/C++ linker. `dev` and `--r2r` do **not** require it.

Verify before relying on AOT (or when publish fails with a platform linker / VC++ error):

```bash
dnx go -- check
```

If the check fails, install the tools for your OS and re-run `check`:

| OS | Fix |
|----|-----|
| **Windows** | `dnx vs -- install --passive --sku:build` |
| **Ubuntu / Debian** | `sudo apt-get install -y build-essential` |
| **macOS** | `xcode-select --install` |

Typical failure without tools: `Platform linker ('clang' or 'gcc') not found` (Linux/macOS) or missing Visual C++ build tools (Windows).

### Step 2: Write the app file

Create an entry-point `.cs` file with top-level statements. Place it outside directories that contain a `.csproj` to avoid project conflicts.

```csharp
// hello.cs
Console.WriteLine("Hello from go#!");

var numbers = new[] { 1, 2, 3, 4, 5 };
Console.WriteLine($"Sum: {numbers.Sum()}");
```

Guidelines:

- Use top-level statements (no `Main`, class, or namespace boilerplate)
- Place `using` directives at the top (after any `#:` directives)
- Place type declarations (classes, records, enums) after all top-level statements
- No shebang—execution is always through `dnx go`

### Step 3: Run with go#

#### While writing, tweaking, or debugging

```bash
dnx go -- dev app.cs
dnx go -- dev app.cs arg1 arg2 "multi word arg"
```

Uses the optimized `dotnet run` path. Prefer this for every edit–run cycle until behavior is correct.

#### Once the script is stable

```bash
dnx go -- app.cs
dnx go -- app.cs arg1 arg2 "multi word arg"
```

**Use this form exclusively for all subsequent runs.** Publishes with native AOT by default, caches outputs, and re-runs the published binary when inputs are unchanged—best for repeated agent invocations.

| Situation | Command |
|-----------|---------|
| Editing, fixing errors, exploring APIs | `dnx go -- dev app.cs` |
| Script correct; re-run as a tool | `dnx go -- app.cs` only |
| Stable but needs reflection/dynamic features | `dnx go -- app.cs --r2r` |

#### ReadyToRun (`--r2r`)

When the stable (publish) path fails because of AOT limits (heavy reflection, dynamic loading, etc.), publish ReadyToRun instead of native AOT:

```bash
dnx go -- app.cs --r2r
dnx go -- app.cs --r2r arg1 arg2
```

This sets `/p:PublishAot=false` and `/p:PublishReadyToRun=true`. `--r2r` is ignored under `dev` (dev always uses `dotnet run`).

#### Diagnostics fallback

For full SDK diagnostics—verbose MSBuild logs, ad-hoc property overrides, or isolating whether an issue is in go# vs the app—fall back to the file-based app host:

```bash
dotnet --file app.cs
dotnet build app.cs -v:n
dotnet run --file app.cs -- arg1
```

Return to `dnx go` once the issue is understood. Day-to-day agent runs stay on go#.

### Step 4: Add directives (if needed)

Place `#:` directives at the top of the file, before any `using` or other C# code.

#### `#:package` — NuGet package references

Specify a version unless the app intentionally uses central package management. Use `@*` for latest (or `@*-*` for pre-release):

```csharp
#:package Humanizer@2.14.1

using Humanizer;

Console.WriteLine("hello world".Titleize());
```

#### `#:property` — MSBuild properties

Syntax: `#:property PropertyName=Value`

```csharp
#:property AllowUnsafeBlocks=true
#:property PublishAot=false
#:property NoWarn=CS0162
```

MSBuild expressions and property functions are supported:

```csharp
#:property LogLevel=$([MSBuild]::ValueOrDefault('$(LOG_LEVEL)', 'Information'))
```

Common properties:

| Property | Purpose |
|----------|---------|
| `AllowUnsafeBlocks=true` | Enable `unsafe` code |
| `PublishAot=false` | Disable native AOT (enabled by default on publish) |
| `NoWarn=CS0162;CS0219` | Suppress specific warnings |
| `LangVersion=preview` | Enable preview language features |
| `InvariantGlobalization=false` | Enable culture-specific globalization |

#### `#:project` — Project references

```csharp
#:project ../MyLibrary/MyLibrary.csproj
```

#### `#:ref` — File-based app references

Reference another `.cs` file as its own virtual project (assembly boundary). Use `#:include` for ordinary helpers in the same assembly; use `#:ref` for project-reference-like boundaries.

```csharp
#:property ExperimentalFileBasedProgramEnableRefDirective=true
#:ref ../Shared/Formatter.cs

Console.WriteLine(Formatter.Title("hello world"));
```

Guidelines:

- The referenced file is compiled as its own virtual project and added as a project reference
- Libraries without top-level statements need `#:property OutputType=Library` in that file
- Consumed members must be public across the assembly boundary
- `#:ref` is transitive; relative paths resolve from the file containing the directive
- Some SDKs need `#:property ExperimentalFileBasedProgramEnableRefDirective=true`; drop it if the SDK accepts `#:ref` without it

#### `#:sdk` — SDK selection

```csharp
#:sdk Microsoft.NET.Sdk.Web
```

#### `#:include` and `#:exclude` — Multi-file apps

Requires SDK **10.0.300+**. Include helper sources/assets; exclude unwanted matches.

```csharp
#:include Helpers.cs
#:include Models/*.cs
#:exclude Models/Generated/*.cs

Console.WriteLine(Formatter.Title("hello world"));
```

Guidelines:

- The file passed to go# is the entry point; put top-level statements there
- Put types (classes, records, enums) in included `.cs` files
- Prefer narrow globs (`Helpers.cs`, `Models/*.cs`) over broad recursive globs
- Paths resolve relative to the file containing the directive
- Included files can declare their own `#:` directives (packages, properties, further includes, etc.)
- Avoid duplicate `#:package`, `#:property`, `#:sdk`, `#:include`, and `#:exclude` across the graph unless the kind allows duplicates

Example layout:

```text
scratch/
    hello.cs
    Helpers.cs
    Models/
        Person.cs
```

```csharp
// hello.cs
#:include Helpers.cs
#:include Models/*.cs

var person = new Person("Ada");
Console.WriteLine(Formatter.Title(person.Name));
```

```csharp
// Helpers.cs
static class Formatter
{
    public static string Title(string value) => value.ToUpperInvariant();
}
```

```csharp
// Models/Person.cs
record Person(string Name);
```

While editing this multi-file app: `dnx go -- dev hello.cs`. When stable: `dnx go -- hello.cs` only.

### Step 5: Clean up

Remove app files when the user is done. Clear go# cached artifacts with:

```bash
dnx go -- clean app.cs
dnx go -- clean --all
```

Unused download locations and published binaries are cleaned periodically in the background. Apps run regularly are not affected.

## Remote references

Instead of a local `.cs` file, pass a remote ref. go# downloads (when needed) and uses the local entry point:

```bash
# Public repo defaults (often main + program.cs or first .cs)
dnx go -- kzu/sandbox

# Branch/tag and path
dnx go -- kzu/sandbox@v1.2.3:src/hello.cs

# Full hosts
dnx go -- github.com/kzu/sandbox@main:hello.cs
dnx go -- gist.github.com/kzu/0ac826dc7de666546aaedd38e5965381
dnx go -- gitlab.com/kzu/runcs/-/blob/main/program.cs
```

Same modes after download: default = publish + run; `dev` = run for iteration. Trailing args go to the app.

Discover shared apps by searching GitHub (and similar hosts) for repositories tagged **`go#`**.

### Security (required)

Remote refs execute **arbitrary code** on the machine (same trust model as cloning and running a project).

- **Never** run a remote ref without asking the user first, unless the user has already made clear they trust that user/org (e.g. they supplied the ref or own the repo).
- Prefer signals of safety: known owner/org, stars, recency of maintenance, readable source before run.
- When in doubt, open or summarize the entry-point source and wait for approval.
- To force a fresh download: `dnx go -- clean owner/repo` (path segment does not matter for bundle delete).

```bash
dnx go -- clean kzu/sandbox
dnx go -- clean kzu/sandbox@main:program.cs
```

## Source-generated JSON

Default publish uses native AOT. Reflection-based APIs like `JsonSerializer.Serialize<T>(value)` fail at runtime under AOT. Use source-generated serialization, or run with `--r2r` if AOT is not required:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

var person = new Person("Alice", 30);
var json = JsonSerializer.Serialize(person, AppJsonContext.Default.Person);
Console.WriteLine(json);

var deserialized = JsonSerializer.Deserialize(json, AppJsonContext.Default.Person);
Console.WriteLine($"Name: {deserialized!.Name}, Age: {deserialized.Age}");

record Person(string Name, int Age);

[JsonSerializable(typeof(Person))]
partial class AppJsonContext : JsonSerializerContext;
```

## Validation

- [ ] `dotnet --version` reports 10.0 or later
- [ ] Multi-file `#:include` / `#:exclude` only if SDK is 10.0.300 or later
- [ ] For default (AOT) publish: `dnx go -- check` succeeds
- [ ] While iterating: `dnx go -- dev app.cs` produces the expected result
- [ ] After the last successful edit: `dnx go -- app.cs` works, and **further runs use this form only**
- [ ] App arguments are not confused with go flags (`--r2r` applies to go, then app args)
- [ ] AOT issues addressed with source-generated JSON or `--r2r`
- [ ] Remote refs only after user trust/approval
- [ ] Clean with `dnx go -- clean …` when discarding work

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Using `dotnet app.cs` for day-to-day runs | Use `dnx go -- app.cs` (stable) or `dnx go -- dev app.cs` (tweaking) |
| Staying on `dev` after the script is done | Switch to `dnx go -- app.cs` exclusively |
| Expecting AOT under `dev` | `dev` is non-AOT; AOT applies to the default publish path |
| Platform linker / VC++ missing on stable AOT publish | `dnx go -- check`, then install per the printed fix (Windows: `dnx vs -- install --passive --sku:build`; Ubuntu: `build-essential`; macOS: Xcode CLT) |
| AOT failures on stable runs (reflection/dynamic) | Source-generated JSON or `dnx go -- app.cs --r2r` |
| Need verbose SDK/MSBuild diagnostics | Fall back to `dotnet --file app.cs` / `dotnet build app.cs -v:n` |
| Running a remote ref without consent | Ask the user; check owner/stars/source first |
| `.cs` file inside a directory with a `.csproj` | Move the app outside the project directory |
| `#:package` without a version | Specify a version: `#:package PackageName@1.2.3` or `@*` for latest |
| `#:property` with wrong syntax | `PropertyName=Value` with no spaces around `=` and no quotes |
| Directives placed after C# code | All `#:` directives must appear before any `using` or other C# statements |
| Helper file is not compiled | Add `#:include Helper.cs` (or a suitable glob) on the entry point |
| Shared file needs an assembly boundary | Use `#:ref` + `#:property OutputType=Library` on the library file if needed |
| Broad include pulls in unrelated files | Narrow patterns; use `#:exclude` for generated/backup files |
| Duplicate directives in included files | Keep package/property/SDK/include/exclude unique across the graph |
| Unexpected build behavior from parent dirs | File-based apps inherit `global.json`, `Directory.Build.*`, and `nuget.config`—isolate the app if settings conflict |

## More info

- go#: https://github.com/devlooped/go and NuGet package [`go`](https://www.nuget.org/packages/go)
- File-based apps: https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps
