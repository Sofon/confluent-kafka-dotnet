# Developer Notes

This document provides information useful to developers working on confluent-kafka-dotnet.


## Building

Nuget packages are built automatically by Semaphore CI corresponding to every commit to a PR or master branch as well as release tags. For further details, inspect the [.semaphore/semaphore.yml](.semaphore/semaphore.yml) file.


## Tests

### Unit Tests

There are unit test suites corresponding to each nuget package. These are [Confluent.Kafka.UnitTests](test/Confluent.Kafka.UnitTests), 
[Confluent.SchemaRegistry.UnitTests](test/Confluent.SchemaRegistry.UnitTests) and
[Confluent.SchemaRegistry.Serdes.UnitTests](test/Confluent.SchemaRegistry.Serdes.UnitTests). To execute, enter the
relevant directory and run:

```
dotnet test
```

### Integration Tests

From the test/docker directory bring up the Kafka cluster with two schema registry instances (one with basic auth enabled, one without).

```
docker-compose up
```

There are integration test suites corresponding to each nuget package. These are [Confluent.Kafka.IntegrationTests](test/Confluent.Kafka.IntegrationTests), 
[Confluent.SchemaRegistry.IntegrationTests](test/Confluent.SchemaRegistry.IntegrationTests) and
[Confluent.SchemaRegistry.Serdes.IntegrationTests](test/Confluent.SchemaRegistry.Serdes.IntegrationTests).

To execute, enter the relevant directory and run:

```
dotnet test
```

## Single-DLL builds

By default, `Confluent.Kafka.csproj` builds a self-contained `Confluent.Kafka.dll`:
the librdkafka native libraries and (for the netstandard2.0/net462 targets) the
managed dependency assemblies (`System.Memory` etc.) are gzip-compressed and
embedded into the assembly as resources instead of being deployed next to it.
At runtime the native libraries are extracted to a per-librdkafka-version cache
directory (`%TEMP%/confluent-kafka-dotnet/...` by default, override with the
`CONFLUENT_KAFKA_LIBRDKAFKA_EXTRACT_DIR` environment variable) and loaded
automatically; embedded managed dependencies are only used when the runtime
cannot find them through regular means.

```
dotnet build -c Release src/Confluent.Kafka/Confluent.Kafka.csproj
```

MSBuild properties:

- `EmbedLibrdkafka` (default `true`) - set to `false` to restore the stock
  behaviour (librdkafka.redist native libraries copied next to the assembly).
- `EmbedLibrdkafkaRuntimes` (default: all runtimes shipped in librdkafka.redist)
  - semicolon separated list of runtime identifiers to embed. Embedding all
  runtimes results in a ~43 MB assembly; embedding a single runtime, e.g.
  `-p:EmbedLibrdkafkaRuntimes=win-x64`, reduces it to ~5 MB.

Explicitly loading librdkafka from a custom path via `Library.Load(path)`
still takes precedence over the embedded libraries.
