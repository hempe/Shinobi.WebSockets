```

BenchmarkDotNet v0.15.2, Linux Manjaro Linux
Intel Core i7-8565U CPU 1.80GHz (Whiskey Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.105
  [Host]   : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  

```
| Method                          | ClientCount | Mean         | Error         | StdDev       | Median      | Rank | Gen0   | Allocated |
|-------------------------------- |------------ |-------------:|--------------:|-------------:|------------:|-----:|-------:|----------:|
| SendReceive_SmallTextMessages   | 1           |     79.09 ns |      8.978 ns |     23.65 ns |    71.17 ns |    1 |      - |     144 B |
| SendReceive_SmallTextMessages   | 10          |    114.86 ns |     10.659 ns |     27.52 ns |   101.27 ns |    2 |      - |     144 B |
| SendReceive_SmallTextMessages   | 5           |    119.30 ns |     25.899 ns |     68.68 ns |    88.08 ns |    2 |      - |     144 B |
| SendReceive_MediumJsonMessages  | 10          |  2,614.70 ns |    132.085 ns |    354.84 ns | 2,566.68 ns |    3 | 0.4883 |    2280 B |
| SendReceive_LargeBinaryMessages | 1           |  3,458.52 ns |    555.417 ns |  1,482.52 ns | 2,954.38 ns |    4 |      - |   16512 B |
| SendReceive_LargeBinaryMessages | 5           |  4,454.70 ns |    657.390 ns |  1,788.48 ns | 3,940.22 ns |    5 |      - |   16512 B |
| SendReceive_LargeBinaryMessages | 10          |  4,653.16 ns |    806.757 ns |  2,181.11 ns | 3,668.83 ns |    5 |      - |   16512 B |
| SendReceive_MediumJsonMessages  | 1           | 19,855.98 ns |  7,629.427 ns | 22,375.79 ns | 2,728.53 ns |    5 | 0.4883 |    2280 B |
| SendReceive_MediumJsonMessages  | 5           | 38,541.58 ns | 20,903.582 ns | 61,306.59 ns | 2,691.10 ns |    5 | 0.4883 |    2280 B |
