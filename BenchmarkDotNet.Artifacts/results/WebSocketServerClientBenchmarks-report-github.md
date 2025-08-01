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
| SendReceive_SmallTextMessagesAsync   | 1           |    63.10 ns |  0.413 ns |  0.366 ns |    1 | 0.0343 |      - |     144 B |
| SendReceive_SmallTextMessagesAsync   | 5           |    76.87 ns |  0.775 ns |  0.725 ns |    2 | 0.0343 |      - |     144 B |
| SendReceive_SmallTextMessagesAsync   | 10          |    95.34 ns |  0.311 ns |  0.243 ns |    3 | 0.0343 |      - |     144 B |
| SendReceive_MediumJsonMessagesAsync  | 10          | 2,030.61 ns |  9.208 ns |  7.189 ns |    4 | 0.5417 |      - |    2280 B |
| SendReceive_MediumJsonMessagesAsync  | 1           | 2,045.28 ns |  8.392 ns |  7.439 ns |    4 | 0.5417 |      - |    2280 B |
| SendReceive_MediumJsonMessagesAsync  | 5           | 2,104.34 ns | 13.181 ns | 11.006 ns |    4 | 0.5417 |      - |    2280 B |
| SendReceive_LargeBinaryMessagesAsync | 1           | 2,781.16 ns | 15.426 ns | 14.429 ns |    5 | 3.9368 | 0.2174 |   16512 B |
| SendReceive_LargeBinaryMessagesAsync | 5           | 2,907.33 ns | 14.994 ns | 13.292 ns |    6 | 3.9368 | 0.2174 |   16512 B |
| SendReceive_LargeBinaryMessagesAsync | 10          | 2,995.17 ns |  9.325 ns |  8.266 ns |    7 | 3.9368 | 0.2174 |   16512 B |
