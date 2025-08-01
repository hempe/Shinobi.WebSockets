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
| ClientToServer_MediumMessagesAsync | 50           |   182.1 μs |  3.09 μs |  2.89 μs | 12.2070 |  49.22 KB |
| ClientToServer_SmallMessagesAsync  | 50           |   349.9 μs |  6.94 μs |  9.96 μs | 12.2070 |   50.8 KB |
| ClientToServer_MediumMessagesAsync | 100          |   371.1 μs |  5.10 μs |  4.26 μs | 23.9258 |  97.79 KB |
| ClientToServer_SmallMessagesAsync  | 100          |   641.7 μs | 12.81 μs | 18.37 μs | 23.4375 |  95.67 KB |
| ClientToServer_MediumMessagesAsync | 250          |   931.9 μs | 18.46 μs | 17.27 μs | 59.5703 | 242.77 KB |
| ClientToServer_SmallMessagesAsync  | 250          | 1,638.7 μs | 31.75 μs | 49.44 μs | 58.5938 | 244.31 KB |
