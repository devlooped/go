// This file is excluded via #:exclude includes/**/internal-only.cs
namespace Showcase.Internal;

public static class InternalOnly
{
    public static string Secret => "should not compile";
}