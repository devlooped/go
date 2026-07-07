#!/usr/bin/env dotnet
#:property TargetFramework=net10.0

#:include common.cs
#:include features/*.cs
#:include sub/**/*.cs

using GlobInclude;

Console.WriteLine("glob: " + Helper.Message() + " | " + Feature1.Id + " | " + Deep.Value + " (via #:include glob)");