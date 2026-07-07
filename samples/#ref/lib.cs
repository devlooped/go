#:property TargetFramework=net10.0
#:property OutputType=Library
#:property ExperimentalFileBasedProgramEnableRefDirective=true

namespace PerfRef;

public static class Greeter
{
    public static string Greet(string name) => $"hello from separate #:ref assembly, {name}";
}
