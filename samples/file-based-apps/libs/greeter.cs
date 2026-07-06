#:property OutputType=Library
#:package Spectre.Console@*

using Spectre.Console;

namespace ShowcaseLib;

public static class Greeter
{
    public static string Greet(string name) =>
        $"[green]{Markup.Escape($"Hello from #:ref greeter.cs, {name}!")}[/]";
}