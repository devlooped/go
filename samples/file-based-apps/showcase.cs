#!/usr/bin/env dotnet
// .NET 10+ file-based app showcase
// Demonstrates #:property, #:package, #:include, #:ref, launch profiles, and AppContext metadata.

#:property TargetFramework=net11.0
#:property LangVersion=preview
#:property ExperimentalFileBasedProgramEnableRefDirective=true
//#:property PublishAot=false
//#:property PackAsTool=false
//#:property LogLevel=$([MSBuild]::ValueOrDefault('$(LOG_LEVEL)', 'Information'))

#:package Spectre.Console@*

#:include config.json
#:include includes/models/task-item.cs
#:include includes/extensions/*.cs
#:exclude includes/**/internal-only.cs

#:ref libs/greeter.cs
#:ref libs/mathlib.cs

using Spectre.Console;
using Showcase.Extensions;
using ShowcaseLib;
using Showcase.Models;

var name = args.Length > 0 ? args[0] : "World";
var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "Information";

AnsiConsole.MarkupLine("[bold cyan].NET file-based app showcase[/]");
AnsiConsole.MarkupLine($"[grey]Log level (from #:property):[/] {logLevel}");

// .NET 10: AppContext metadata from the virtual project
var entryPath = AppContext.GetData("EntryPointFilePath") as string;
var entryDir = AppContext.GetData("EntryPointFileDirectoryPath") as string;
AnsiConsole.MarkupLine($"[grey]Entry point:[/] {entryPath}");
AnsiConsole.MarkupLine($"[grey]Entry directory:[/] {entryDir}");

// .NET 11 #:ref — separate assemblies compiled from sibling .cs files
AnsiConsole.MarkupLine(Greeter.Greet(name));
AnsiConsole.MarkupLine($"[yellow]2 + 3 = {MathLib.Add(2, 3)}[/] (via transitive #:ref to utils.cs)");

// .NET 11 #:include — types merged into this compilation
var tasks = new[]
{
    new TaskItem(1, "Exercise #:include", true),
    new TaskItem(2, "Exercise #:ref", false),
    new TaskItem(3, "Run with dotnet showcase.cs", true),
};

var table = new Table().AddColumn("Id").AddColumn("Task").AddColumn("Done");
foreach (var task in tasks)
{
    table.AddRow(
        task.Id.ToString(),
        task.Title.Emphasize(),
        task.IsDone ? "[green]yes[/]" : "[red]no[/]");
}

AnsiConsole.Write(table);

// #:include config.json is available on disk next to the entry file
var configPath = Path.Combine(entryDir ?? ".", "config.json");
if (File.Exists(configPath))
{
    var config = await File.ReadAllTextAsync(configPath);
    AnsiConsole.MarkupLine($"[grey]Loaded config.json ({config.Length} chars)[/]");
}

AnsiConsole.MarkupLine("[green]Done.[/] Try: [grey]dotnet run showcase.cs -- You[/]");