#!/usr/bin/env dotnet
#:property TargetFramework=net10.0

#:include lib.cs

using PerfInclude;

Console.WriteLine("include: " + Helper.Message() + " (via #:include)");