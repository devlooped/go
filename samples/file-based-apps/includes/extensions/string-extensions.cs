namespace Showcase.Extensions;

public static class StringExtensions
{
    public static string Emphasize(this string value) => $"[bold]{value}[/]";
}