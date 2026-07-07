#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property ExperimentalFileBasedProgramEnableRefDirective=true

#:ref lib.cs

using PerfRef;

Console.WriteLine("ref: " + Greeter.Greet("perf") + " (via #:ref)");