```shell
Usage: [command] [arguments...] [options...] [-h|--help] [--version]

Runs a file-based .NET app from a .cs entrypoint.

Arguments:
  [0] <string>      Path to an existing .cs file or remote ref (owner/repo[@ref][:path]).
  [1] <string[]>    Arguments before '--' are passed to 'dotnet publish'; arguments after '--' are forwarded to the published app. Without '--', all extra arguments are forwarded to the published app.

Options:
  --r2r      Publish with ReadyToRun instead of native AOT; supports more dynamic .NET features while keeping most publish optimizations. Alias: --go-r2r.
  --force    Force re-download of remote ref if applicable. Alias: --go-force.
  --debug    Launch debugger before executing. Alias: --go-debug.

Commands:
  clean    Deletes cached publish artifacts for a file-based .NET app.
  dev      Runs a file-based .NET app from a .cs entrypoint using dotnet run for fast iteration.
```
