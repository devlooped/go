#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property ExperimentalFileBasedProgramEnableRefDirective=true

#:include inclib.cs
#:ref reflib.cs

using PerfInc;
using PerfRef;

Console.WriteLine("incref: " + Helper.Message() + " | " + Greeter.Greet("both") + " (via #:include + #:ref)");