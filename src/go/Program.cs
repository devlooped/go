using Devlooped;

if (args is ["--help" or "-h" or "-?"])
{
    Console.WriteLine("Locates the dotnet executable used to run this tool.");
    Console.WriteLine();
    Console.WriteLine("Usage: go [--help]");
    return 0;
}

var path = DotnetMuxer.Path?.FullName;
if (path is null)
{
    Console.Error.WriteLine("dotnet executable not found.");
    return 1;
}

Console.WriteLine(path);
return 0;