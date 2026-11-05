---
chapter: 19
part: "Part IV — Advanced Enterprise Implementations"
title: "Automated Testing and Code Quality"
build-summary: "Harbourline Connect has 91 fast unit tests that run on every push, a short list of integration checks against a developer environment, and a build that refuses nullable, analyzer and style problems."
builds:
  - "Unit tests with xUnit v3 and NSubstitute, substituting IOrganizationServiceAsync2"
  - "Helpers that fabricate Dataverse faults and rows for tests"
  - "A quality gate: nullable references, analyzers, warnings as errors, CI"
versions: ".NET 10 · xunit.v3 4.0.2 · NSubstitute 6.2.0 · Microsoft Testing Platform"
---

::: objectives
- Explain the architecture choices that make integration code testable.
- Substitute `IOrganizationServiceAsync2` and assert on the requests your code sends.
- Fabricate Dataverse faults to test error handling.
- Write data-driven tests with `[Theory]`, and test time-dependent code with a fixed clock.
- Decide what belongs in unit tests, integration tests and smoke tests.
- Enforce quality with nullable reference types, analyzers and CI.
:::

## The business problem

Integration code fails at the edges: a fault code nobody handled, a page nobody fetched, a status nobody expected. Those edges are expensive to reach by hand, because each needs a particular state in Dataverse or a partner misbehaving on cue. Harbourline's old integration had no tests, so every change was tested in production, by the finance team.

## How it works

### Testable by design

Three choices from earlier chapters make Harbourline Connect testable:

- **Core has no Functions types.** Business logic lives in classes that take their dependencies through constructors.
- **Dependencies are interfaces.** Services depend on `IOrganizationServiceAsync2`, `ILedgerOneClient`, `ICheckpointStore` and `TimeProvider`, not on concrete clients or `DateTime.UtcNow`.
- **Functions are thin.** They translate between triggers and services, so there's little logic left in them to test.

A **test double** stands in for a dependency. **NSubstitute** creates them from interfaces and abstract classes: you configure what a call returns (or throws), then check which calls were received.

### What to test where

::: {#tbl:levels caption="Test levels in Harbourline Connect"}
| Level | What it proves | Runs | Example |
|---|---|---|---|
| **Unit** | Our logic: mapping, validation, decisions, error handling | Every push, seconds | `PropertyServiceTests` |
| **Integration** | Our code works with real Dataverse, Service Bus, partners | Before release, against dev/test | Probe, migration rehearsal, `.http` requests |
| **Smoke** | A deployment is alive and wired | After every deployment | `/api/health`, one real request per area |
:::

Unit tests can't prove that Dataverse accepts a request; they prove that your code sends the request you meant and handles every answer. Integration tests prove the rest, and are fewer and slower.

### xUnit v3 and the Microsoft Testing Platform

The test project uses **xUnit v3**, whose test projects are executables, running on the **Microsoft Testing Platform**, selected in `global.json`. `dotnet test --solution HarbourlineConnect.slnx` runs them locally and in CI. `TestContext.Current.CancellationToken` gives each test a token that is cancelled if the run is aborted; xUnit's analyzers remind you to pass it.

## Architecture

The test project references Core and Contracts only, never the Functions project. `tests/Harbourline.Connect.UnitTests/Support` holds two helpers: `Faults` to build Dataverse exceptions and `Rows` to build entities.

## Prerequisites

- The solution builds (Chapter 3).
- Familiarity with the services from Chapters 7, 10, 15, 16 and 21.

## Step-by-step implementation

### Step 1 – Fabricate Dataverse faults

`ServiceClient` reports Dataverse errors as `FaultException<OrganizationServiceFault>`. Tests build the same exception (@lst:fault).

```{.csharp #lst:fault include="tests/Harbourline.Connect.UnitTests/Support/Faults.cs" region="test-fault" caption="Building the exception Dataverse throws"}
```

With it, a test can make any call fail with any documented error code: not found, duplicate key, version mismatch, privilege denied, throttling.

### Step 2 – Substitute Dataverse and assert on the request

```{.csharp #lst:testlist include="tests/Harbourline.Connect.UnitTests/Properties/PropertyServiceTests.cs" region="test-list" caption="Arrange a result, act, then assert on the query Dataverse received"}
```

1. The substitute returns one row and says more pages exist.
2. `Received(1)` with `Arg.Is` checks the query the service built: page size and number of conditions. A wrong filter fails here, without Dataverse.

### Step 3 – Test error handling

```{.csharp #lst:testconc include="tests/Harbourline.Connect.UnitTests/Properties/PropertyServiceTests.cs" region="test-concurrency" caption="A version mismatch becomes PreconditionFailed"}
```

1. Dataverse's documented error for a stale row version.
2. The test also checks that the request asked for the concurrency check and carried the caller's version. Both halves of optimistic concurrency are covered.

### Step 4 – Data-driven tests

```{.csharp #lst:theory include="tests/Harbourline.Connect.UnitTests/Properties/PropertyValidatorTests.cs" region="test-theory" caption="One test, several inputs"}
```

`[Theory]` with `[InlineData]` runs the test once per case and reports each separately. For cases that aren't compile-time constants, such as exceptions, `[MemberData]` with `TheoryData` does the same (@lst:classify).

```{.csharp #lst:classify include="tests/Harbourline.Connect.UnitTests/Orders/OrderTests.cs" region="test-classifier" caption="Every kind of failure, classified"}
```

### Step 5 – Control time

Code that uses the clock is tested with a substituted `TimeProvider`: `time.GetUtcNow().Returns(fixedInstant)`. The invoice sync test (Chapter 10) uses it to check exactly which watermark is saved; the health check uses it to check the reported time.

### Step 6 – Test behaviour, not implementation

Assert on what matters to callers and partners: the outcome, the request sent, the checkpoint saved. Avoid asserting the order of internal calls unless order *is* the requirement, as in the order pipeline, where the Dataverse record must exist before LedgerOne is called (`Received.InOrder`). Tests that mirror the implementation line by line break on every refactoring and catch few bugs.

### Step 7 – Test real file formats

Some code is best tested against the real thing, in memory. The Excel tests write a workbook, then read it back with a library; the feed tests parse real CSV, JSON and XML strings; the event tests parse a context shaped like Microsoft's published example. No mocks, no disk, still fast.

### Step 8 – Enforce quality in the build

`Directory.Build.props` applies to every project:

- **Nullable reference types**: the compiler tracks which references can be null.
- **`AnalysisLevel` latest**: the .NET analyzers' current rules.
- **`TreatWarningsAsErrors`**: a warning fails the build, except NuGet audit warnings, which CI reports without blocking.

`.editorconfig` sets formatting and naming, and `max_line_length = 72` keeps code readable in print and side-by-side diffs. CI (`.github/workflows/azure-functions-book.yml`) builds, tests and compiles Bicep on every push, and annotates the run with errors and outdated packages.

### Step 9 – Integration and smoke checks

Keep the slow checks short and scripted:

1. **Developer environment:** run the probe (Chapter 4) and the `.http` files for each API.
2. **Test environment:** migration rehearsal and reconciliation (Chapter 16); a bulk load measurement (Chapter 15); the gateway script (Chapter 14).
3. **After every deployment:** the pipeline's smoke test calls `/api/health` and fails the release if it doesn't answer (Chapter 20).

## Screens you will use

::: {.capture #fig:ci-run caption="The CI run on a pull request: build, tests and Bicep compiled, with annotations." app="GitHub" path="Repository › Actions › azure-functions-book › a run" annotations="1 Build; 2 Test summary notice; 3 Bicep notice; 4 Outdated package notices"}
:::

::: {.capture #fig:test-explorer caption="Tests in the IDE, grouped by class." app="VS Code (C# Dev Kit) or Visual Studio" path="Testing view" annotations="1 Test classes; 2 A theory's cases; 3 Run and debug buttons"}
:::

## Complete working code

- `tests/Harbourline.Connect.UnitTests/`: tests for every Core area.
- `Directory.Build.props`, `.editorconfig`, `global.json`
- `.github/workflows/azure-functions-book.yml`

## Configuration

::: {#tbl:settings caption="Quality settings"}
| Setting | File | Value |
|---|---|---|
| `Nullable` | `Directory.Build.props` | `enable` |
| `AnalysisLevel` | `Directory.Build.props` | `latest` |
| `TreatWarningsAsErrors` | `Directory.Build.props` | `true` (NU1901–NU1904 excepted) |
| Test runner | `global.json` | `Microsoft.Testing.Platform` |
:::

## Sample input and output

```text
$ dotnet test --solution HarbourlineConnect.slnx -c Release
  Harbourline.Connect.UnitTests  passed (2s 250ms)
Test run summary: Passed!
  total: 91  failed: 0  succeeded: 91  skipped: 0
```

## Testing

This chapter is about testing; its own check is CI. A pull request that breaks a test, adds a nullable warning or a Bicep error can't turn green.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see with tests and builds"}
| Symptom | Likely cause | Fix |
|---|---|---|
| `xUnit1051` warning fails the build | A call that takes a `CancellationToken` wasn't given one | Pass `TestContext.Current.CancellationToken` |
| `dotnet test` says VSTest isn't supported | Test runner not set | Set `"test": {"runner": "Microsoft.Testing.Platform"}` in `global.json` |
| Substitute returns null unexpectedly | Arguments didn't match the configured call | Use `Arg.Any<T>()` for parameters you don't care about |
| Test passes alone, fails in a run | Shared static state | Create everything per test |
| Build fails only in CI | Different SDK or warnings | Use `global.json`; build with `-c Release` locally |
:::

## Security considerations

::: security
Tests use fictional data only, never copies of production rows, and no real credentials. Integration tests run against developer or test environments with identities that can't reach production.
:::

## Performance and scalability

::: performance
A fast suite gets run. Keep unit tests in-memory, with no sleeps and no network; Harbourline's 91 take about two seconds. Performance itself is measured separately, with the method in Chapter 15, never inferred from unit tests.
:::

## Alternatives and design decisions

FakeXrmEasy and similar libraries simulate an in-memory Dataverse, which suits plug-in testing and query-heavy logic; check their licences. Harbourline uses substitutes because most of its logic is mapping, decisions and error handling, where asserting on the request is clearer. Moq is a common alternative to NSubstitute; either works.

## Best practices

- Keep business logic out of function entry points, behind interfaces.
- Assert on outcomes and requests, not on internal call sequences.
- Build Dataverse faults with real error codes and test every handled code.
- Substitute the clock with `TimeProvider`.
- Run unit tests on every push; script integration and smoke checks.
- Treat warnings as errors and keep the build green.

## Hands-on exercise

::: exercise
**Goal:** cover a missing case.

**Steps:** add a test that `PropertyService.UpdateAsync` rethrows a privilege-denied fault rather than returning an outcome, and a test that `ContinuationToken.Decode` rejects a token whose page number is 1.

**Done when:** both tests pass, and both fail if you comment out the code they protect.

**Stretch:** write a test for `CustomerSyncService.PushAsync` proving that an account whose hash equals its stored hash is not sent to LedgerOne.
:::

## Knowledge check

::: knowledge-check
1. Which three design choices make Harbourline Connect testable?
2. What does a unit test of `PropertyService` prove, and what can't it prove?
3. How do you make a substitute throw the error Dataverse returns for a stale version?
4. Why are `Received.InOrder` assertions used sparingly?
5. What does `TreatWarningsAsErrors` protect against?
:::

Answers are in Appendix G.

::: summary
- Testability comes from design: thin functions, logic in Core, dependencies as interfaces.
- NSubstitute stands in for Dataverse; tests assert on outcomes and on the requests sent.
- Real Dataverse fault codes make error-handling tests meaningful.
- Unit, integration and smoke checks each prove different things.
- Nullable references, analyzers, warnings as errors and CI keep quality from drifting.
:::

## References

- [Unit testing C# with xUnit](https://learn.microsoft.com/dotnet/core/testing/unit-testing-csharp-with-xunit) – Microsoft Learn, checked 2026-10-27.
- [Microsoft.Testing.Platform overview](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro) – Microsoft Learn, checked 2026-10-27.
- [Testing with dotnet test](https://learn.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test) – Microsoft Learn, checked 2026-10-27.
- [Nullable reference types](https://learn.microsoft.com/dotnet/csharp/nullable-references) – Microsoft Learn, checked 2026-10-27.
- [Code analysis in .NET](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview) – Microsoft Learn, checked 2026-10-27.
- [NSubstitute documentation](https://nsubstitute.github.io/) – NSubstitute project, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: test double, substitute, theory, smoke test, integration test, nullable reference types, analyzer -->
