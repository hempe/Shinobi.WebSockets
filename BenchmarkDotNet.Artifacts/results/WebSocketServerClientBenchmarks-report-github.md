```

BenchmarkDotNet v0.15.2, Linux Manjaro Linux
Intel Core i7-8565U CPU 1.80GHz (Whiskey Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 9.0.105
  [Host]   : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2

Job=.NET 9.0  Runtime=.NET 9.0  

```
| Method                               | ClientCount | Mean        | Error     | StdDev    | Rank | Gen0   | Gen1   | Allocated |
|------------------------------------- |------------ |------------:|----------:|----------:|-----:|-------:|-------:|----------:|
| SendReceive_SmallTextMessagesAsync   | 1           |    60.03 ns |  0.681 ns |  0.637 ns |    1 | 0.0343 |      - |     144 B |
| SendReceive_SmallTextMessagesAsync   | 5           |    77.83 ns |  0.308 ns |  0.241 ns |    2 | 0.0343 |      - |     144 B |
| SendReceive_SmallTextMessagesAsync   | 10          |   103.20 ns |  0.523 ns |  0.408 ns |    3 | 0.0343 |      - |     144 B |
| SendReceive_MediumJsonMessagesAsync  | 5           | 2,017.86 ns | 18.321 ns | 17.138 ns |    4 | 0.5417 |      - |    2280 B |
| SendReceive_MediumJsonMessagesAsync  | 1           | 2,044.25 ns |  5.294 ns |  4.421 ns |    4 | 0.5417 |      - |    2280 B |
| SendReceive_MediumJsonMessagesAsync  | 10          | 2,125.20 ns | 12.489 ns | 11.071 ns |    5 | 0.5417 |      - |    2280 B |
| SendReceive_LargeBinaryMessagesAsync | 1           | 2,919.62 ns | 14.194 ns | 13.277 ns |    6 | 3.9368 | 0.2174 |   16512 B |
| SendReceive_LargeBinaryMessagesAsync | 5           | 2,920.29 ns | 17.200 ns | 14.363 ns |    6 | 3.9368 | 0.2174 |   16512 B |
| SendReceive_LargeBinaryMessagesAsync | 10          | 2,966.10 ns |  7.019 ns |  5.861 ns |    6 | 3.9368 | 0.2174 |   16512 B |
