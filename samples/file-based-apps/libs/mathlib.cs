#:property OutputType=Library
#:property ExperimentalFileBasedProgramEnableRefDirective=true
#:ref utils.cs

namespace ShowcaseLib;

public static class MathLib
{
    public static int Add(int a, int b) => Utils.Sum(a, b);
}