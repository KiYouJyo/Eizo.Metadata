# Recognition performance baseline

This document records comparative performance baselines for
`Eizo.Metadata.Recognition`. The numbers are not a hardware-independent SLA. They are
reference points for detecting meaningful regressions using the same benchmark harness.

## Stage 6 baseline

Measured on **2026-09-10** by the GitHub Actions `ubuntu-latest` runner using .NET 10.
The benchmark performs a 2,000-item warm-up and then measures deterministic mixed
workloads containing ordinary episodes, Japanese episode syntax, season/cour folders,
OVA, movie, final/part markers and unresolved technical filenames.

| Items | Elapsed | Throughput | Allocated |
| ---: | ---: | ---: | ---: |
| 1,000 | 59.5 ms | 16,817 items/s | 7.5 MiB |
| 10,000 | 603.6 ms | 16,568 items/s | 74.9 MiB |
| 100,000 | 2,789.3 ms | 35,851 items/s | 677.0 MiB |

The 100K run completed in under three seconds on this particular runner. Allocation
scales approximately linearly with workload size. Throughput is not expected to scale
perfectly between the three sizes because JIT, GC, timer granularity and runner
scheduling affect short and long runs differently.

## Reproduce

```powershell
dotnet run --project benchmarks/Eizo.Metadata.Recognition.Benchmarks/Eizo.Metadata.Recognition.Benchmarks.csproj --configuration Release -- --output artifacts/benchmarks
```

The harness writes:

- `recognition-benchmark.md`
- `recognition-benchmark.json`

CI uploads both files as a per-commit artifact.

## Regression policy

For now, benchmark results are recorded rather than used as a hard timing gate. A
future gate should be based on multiple comparable CI samples and should account for
normal GitHub-hosted-runner variance.

A performance change deserves investigation when it shows one or more of these patterns
across repeated comparable runs:

- materially lower throughput at all three workload sizes;
- super-linear elapsed-time growth;
- sharply increased allocation per item;
- benchmark timeout or regex timeout failures;
- a changed checksum that indicates benchmark behavior changed rather than merely
  performance.

Any intentional benchmark-workload change must update this document so old and new
numbers are not compared as if they measured the same thing.
