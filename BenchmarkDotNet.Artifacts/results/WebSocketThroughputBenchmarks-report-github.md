```

BenchmarkDotNet v0.15.2, Linux Manjaro Linux
Intel Core i7-8565U CPU 1.80GHz (Whiskey Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.105
  [Host]   : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  

```
| Method                             | MessageCount | Mean       | Error    | StdDev   | Gen0    | Allocated |
|----------------------------------- |------------- |-----------:|---------:|---------:|--------:|----------:|
| ClientToServer_MediumMessagesAsync | 50           |   237.1 μs |  4.09 μs |  4.02 μs | 14.8926 |  60.03 KB |
| ClientToServer_SmallMessagesAsync  | 50           |   330.9 μs |  6.58 μs | 12.68 μs | 13.6719 |  55.89 KB |
| ClientToServer_MediumMessagesAsync | 100          |   480.1 μs |  8.29 μs |  7.35 μs | 28.3203 | 118.83 KB |
| ClientToServer_SmallMessagesAsync  | 100          |   642.4 μs | 12.68 μs | 25.32 μs | 27.3438 | 114.04 KB |
| ClientToServer_MediumMessagesAsync | 250          | 1,209.7 μs | 24.07 μs | 27.72 μs | 70.3125 | 293.08 KB |
| ClientToServer_SmallMessagesAsync  | 250          | 1,664.4 μs | 33.07 μs | 64.50 μs | 68.3594 | 278.63 KB |
