```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
AMD Ryzen AI 9 HX 370 w/ Radeon 890M 2.00GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 11.0.100-preview.5.26302.115
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4
  Job-TASYDQ : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=3  LaunchCount=1  
UnrollFactor=1  WarmupCount=1  

```
| Method           | Sample          | Mean       | Error       | StdDev   |
|----------------- |---------------- |-----------:|------------:|---------:|
| **&#39;dnx go&#39;**         | **#include**        |   **483.2 ms** |    **85.70 ms** |  **4.70 ms** |
| &#39;dotnet publish&#39; | #include        | 3,302.0 ms | 1,003.60 ms | 55.01 ms |
| &#39;dnx go dev&#39;     | #include        |   500.1 ms |   250.72 ms | 13.74 ms |
| &#39;dotnet run&#39;     | #include        |   475.6 ms |   100.88 ms |  5.53 ms |
| **&#39;dnx go&#39;**         | **#include + #ref** |   **481.4 ms** |   **236.79 ms** | **12.98 ms** |
| &#39;dotnet publish&#39; | #include + #ref | 3,474.3 ms |   391.66 ms | 21.47 ms |
| &#39;dnx go dev&#39;     | #include + #ref |   498.3 ms |   290.97 ms | 15.95 ms |
| &#39;dotnet run&#39;     | #include + #ref | 1,426.7 ms |   318.56 ms | 17.46 ms |
| **&#39;dnx go&#39;**         | **#ref**            |   **487.1 ms** |   **140.05 ms** |  **7.68 ms** |
| &#39;dotnet publish&#39; | #ref            | 3,509.4 ms |   616.31 ms | 33.78 ms |
| &#39;dnx go dev&#39;     | #ref            |   505.2 ms |   264.59 ms | 14.50 ms |
| &#39;dotnet run&#39;     | #ref            | 1,413.9 ms |   267.93 ms | 14.69 ms |
| **&#39;dnx go&#39;**         | **minimal**         |   **480.8 ms** |   **486.66 ms** | **26.68 ms** |
| &#39;dotnet publish&#39; | minimal         | 3,237.4 ms |   567.55 ms | 31.11 ms |
| &#39;dnx go dev&#39;     | minimal         |   488.4 ms |   209.11 ms | 11.46 ms |
| &#39;dotnet run&#39;     | minimal         |   482.5 ms |   507.46 ms | 27.82 ms |
