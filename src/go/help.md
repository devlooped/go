```shell
Usage: [arguments...] [-h|--help] [--version]

Runs a file-based .NET app from a .cs entrypoint.

Arguments:
  [0] <string>      Path to an existing .cs file.
  [1] <string[]>    Arguments before '--' are passed to 'dotnet publish'; arguments after '--' are forwarded to the published app. 
 Without '--', all extra arguments are forwarded to the published app.
```
