```

BenchmarkDotNet v0.15.2, Linux Manjaro Linux
Intel Core i7-8565U CPU 1.80GHz (Whiskey Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.105
  [Host]   : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  

```
| Method                             | MessageCount | Mean       | Error     | StdDev    | Median     | Gen0    | Allocated |
|----------------------------------- |------------- |-----------:|----------:|----------:|-----------:|--------:|----------:|
| ClientToServer_SmallMessagesAsync  | 50           |   163.0 μs |   9.58 μs |  28.26 μs |   164.3 μs | 14.2822 |  58.32 KB |
| ClientToServer_MediumMessagesAsync | 50           |   228.0 μs |  12.01 μs |  34.66 μs |   216.6 μs | 14.6484 |  59.75 KB |
| ClientToServer_MediumMessagesAsync | 100          |   477.8 μs |  28.95 μs |  84.46 μs |   443.5 μs | 28.3203 | 118.63 KB |
| ClientToServer_SmallMessagesAsync  | 100          |   904.7 μs |  45.73 μs | 134.11 μs |   932.0 μs | 27.3438 | 114.56 KB |
| ClientToServer_MediumMessagesAsync | 250          | 1,217.7 μs |  60.14 μs | 174.48 μs | 1,184.5 μs | 72.2656 | 295.09 KB |
| ClientToServer_SmallMessagesAsync  | 250          | 2,260.6 μs | 100.97 μs | 297.70 μs | 2,242.1 μs | 68.3594 | 279.99 KB |
