```shell
Usage: [command] [arguments...] [options...] [-h|--help] [--version]

Runs a file-based .NET app from a .cs entrypoint.

Arguments:
  [0] <string>      Path to an existing .cs file.
  [1] <string[]>    Arguments before '--' are passed to 'dotnet publish'; arguments after '--' are forwarded to the published app. Without '--', all extra arguments are forwarded to the published app.

Options:
  --r2r    Publish with ReadyToRun instead of native AOT; supports more dynamic .NET features while keeping most publish optimizations.

Commands:
  clean    Deletes cached build artifacts for a file-based .NET app.
  dev      Runs a file-based .NET app from a .cs entrypoint using dotnet run for fast iteration.
```
