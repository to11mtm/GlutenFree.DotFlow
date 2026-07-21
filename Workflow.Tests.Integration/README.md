# Workflow.Tests.Integration 🐳✨

This project hosts **integration tests that require a running Docker daemon**
(via [Testcontainers](https://dotnet.testcontainers.org/)).

Keeping them separate from `Workflow.Tests` means:

- ⚡ The main test project runs fast and works on machines without Docker.
- 🧪 CI can run the suites independently (e.g. quick PR check vs nightly integration run).
- 🧹 Iterative TDD loops don't pay container startup cost unless they need to.

## Current contents

| File | Container |
|------|-----------|
| `Persistence/NatsProviderTests.cs` | NATS (JetStream) |
| `Persistence/PostgresProviderTests.cs` | PostgreSQL |
| `Persistence/S3BlobStoreTests.cs` | MinIO (S3-compatible) |

All tests are tagged `[Trait("Category", "Integration")]` so they can be
filtered selectively:

```pwsh
# Run only Docker-free unit tests
dotnet test Workflow.Tests

# Run Docker-backed integration tests (requires Docker Desktop running)
dotnet test Workflow.Tests.Integration

# Run everything
dotnet test Workflow.sln
```

## Adding a new Docker-backed test

1. Drop the file under a subfolder matching its concern (e.g. `Persistence/`, `Messaging/`).
2. Use namespace `Workflow.Tests.Integration.<Subfolder>`.
3. Mark the class with `[Trait("Category", "Integration")]`.
4. Prefer a single shared container per test class via `IAsyncLifetime`
   (see existing files for the pattern). uwu~ 💖

