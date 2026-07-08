```shell
Usage: [command] [arguments...] [options...] [-h|--help] [--version]

Runs a file-based .NET app from a .cs entrypoint.

Arguments:
  [0] <string>      Path to an existing .cs file or remote ref (owner/repo[@ref][:path]).
  [1] <string[]>    Arguments to pass to the app.

Options:
  --r2r    Publish with ReadyToRun instead of native AOT; supports more dynamic .NET features while keeping most publish optimizations.

Commands:
  clean    Deletes cached publish artifacts for a file-based .NET app, or for a remote ref.
  dev      Runs a file-based .NET app from a .cs entrypoint using dotnet run for fast iteration.
```
