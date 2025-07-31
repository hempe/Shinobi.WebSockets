```

BenchmarkDotNet v0.15.2, Linux Manjaro Linux
Intel Core i7-8565U CPU 1.80GHz (Whiskey Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.105
  [Host]   : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  

```
| Method                        | MessageCount | Mean       | Error    | StdDev    | Gen0    | Allocated |
|------------------------------ |------------- |-----------:|---------:|----------:|--------:|----------:|
| ClientToServer_MediumMessages | 50           |   291.7 μs |  4.84 μs |   7.09 μs | 14.6484 |  59.69 KB |
| ClientToServer_SmallMessages  | 50           |   432.4 μs | 10.51 μs |  30.32 μs | 14.6484 |  60.59 KB |
| ClientToServer_MediumMessages | 100          |   577.1 μs | 10.99 μs |  10.79 μs | 28.3203 | 118.46 KB |
| ClientToServer_SmallMessages  | 100          |   837.2 μs | 16.63 μs |  43.81 μs | 29.2969 | 121.32 KB |
| ClientToServer_MediumMessages | 250          | 1,462.3 μs | 23.46 μs |  19.59 μs | 72.2656 | 295.07 KB |
| ClientToServer_SmallMessages  | 250          | 2,254.4 μs | 44.95 μs | 131.82 μs | 70.3125 | 294.74 KB |
