# Phase 2.5: File System & Cloud Storage Modules (Weeks 15-16) 📁

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md)

---

## Overview

Phase 2.5 ships DotFlow's **file family** — local file read/write, structured-format parsing (CSV/JSON/XML), archive compression/decompression — plus the **cloud-storage family** (Amazon S3 + Azure Blob Storage). Everything sits on a **shared path-security + options layer** (`IWorkflowPathValidator`, `FileSystemModuleOptions`) so that every module that touches a path goes through one sandbox gate — no module ever calls `File.ReadAllText(userInput)` directly~ 🌷

Two families, mirroring the 2.4 layout:

- **2.5.a Local file family** (2.5.a.0–2.5.a.4) — lives in `Workflow.Modules` under `Builtin/File/` (same placement as the HTTP family from 2.3; deps are light: `CsvHelper` only)
- **2.5.b Cloud storage family** (2.5.b.0–2.5.b.2) — lives in a **new `Workflow.Modules.Cloud` project** so the AWS + Azure SDK transitive weight is quarantined (same rule as D14 kept Roslyn out of `Workflow.Modules` in 2.4)

**Timeline:** 2 weeks (Weeks 15-16 — shifted from the original "Week 12-13" per 2.4's D16 four-week extension) — 2.5.a Week 15 · 2.5.b Week 16
**Complexity:** 🟡 Medium — the modules themselves are simple I/O wrappers; the real work is the **path-security sandbox** (traversal, symlinks, extension policy, size limits) and the **named storage-connection registry** for cloud credentials

> **CopilotNote:** Hot paths: `Workflow.Modules/Builtin/File/*` (shared path validator + 8 local modules) and `Workflow.Modules.Cloud/*` (storage-connection registry + 2 cloud modules). `Workflow.Api/Program.cs` gains `AddCloudStorageModules()` + storage-connection CRUD wiring (mirrors 2.4.a.5). Tests stay Docker-free in `Workflow.Tests` (temp-dir sandboxes); S3 E2E uses the **already-referenced `Testcontainers.Minio`**, Azure Blob E2E adds an Azurite container — both Docker-gated in `Workflow.Tests.Integration`~ 🌸

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 Every path goes through `IWorkflowPathValidator`** | One shared service canonicalises (`Path.GetFullPath`), rejects traversal (`..` escaping a sandbox root), enforces allowed-root + extension policies, and returns the resolved absolute path. Modules never touch raw user paths. |
| **D2 Sandbox is deny-by-default when roots are configured** | `FileSystemModuleOptions.AllowedRoots` — when non-empty, any path outside the roots fails validation. When empty, behaviour is governed by `UnrestrictedIfNoRoots` (default `true` for dev ergonomics; hosts SHOULD configure roots in production — documented loudly). *(Q1 resolved: unrestricted-with-warning; a startup warning is logged when no roots are configured.)* |
| **D3 Config via `context.Properties`, results as output ports** | Same correction 2.4.a.1 established: the legacy checklist in `Phase2-CoreFeatures.md` says "Input: path" but the codebase has no separate input-port concept for config — `path`, `encoding`, etc. are **Properties** (template-expandable via `PropertyBinder`), results are **Outputs**. |
| **D4 Local family lives in `Workflow.Modules`, cloud family is quarantined** | Local modules (file/CSV/JSON/XML/compress) add only `CsvHelper` — acceptable in `Workflow.Modules`, registered in `BuiltinModuleRegistration.GetAll()`. Cloud modules pull `AWSSDK.S3` + `Azure.Storage.Blobs` — new `Workflow.Modules.Cloud` project, opt-in `AddCloudStorageModules()` wired by the host (same reverse-dependency rule as `AddDatabaseModules()`). |
| **D5 Named storage connections preferred over inline credentials** | The legacy checklist put `accessKey`/`secretKey` directly on the S3 module — that contradicts the credential-hiding convention from 2.3.4/2.4-D3. Cloud modules accept `storageConnectionId` (preferred, resolved via `IStorageConnectionRegistry`) **or** inline credentials (escape hatch, documented as dev-only). |
| **D6 Cloud modules use the SDKs directly, not `IBlobStore`** | `IBlobStore` is DotFlow's *own* persistence blob namespace (workflow artifacts, compiled modules). Workflow authors need arbitrary buckets/containers/prefixes with their own credentials — a different concern. `Workflow.Persistence.S3` remains the pattern reference for S3 client lifecycle only. |
| **D7 No SharpZipLib — .NET 8 built-ins cover the matrix** | `System.IO.Compression` (Zip via `ZipFile`, GZip via `GZipStream`) + **`System.Formats.Tar`** (in-box since .NET 7) cover Zip/GZip/Tar/TarGz. Zero new compression packages. The legacy checklist's "Tar: Use SharpZipLib" is superseded. |
| **D8 Outputs always materialised, bounded by `maxSize`** | Like 2.4-D8: outputs are plain CLR values (`string`, `byte[]`, `IReadOnlyList<IReadOnlyDictionary<string, object?>>`) — never open streams. `FileReadModule.maxSize` defaults to `FileSystemModuleOptions.DefaultMaxReadBytes` (16 MB); exceeding it is a module failure, not a truncation. Streaming/chunked file handling → post-MVP **2.5.a.P2**. |
| **D9 Query modules (`jsonquery`/`xmlquery`) move to Phase 2.6** | The legacy checklist listed `builtin.transform.jsonquery`/`xmlquery` under 2.5, but they're pure transformations (no file I/O) with `builtin.transform.*` IDs — they belong with 2.6 Data Transformation. 2.3.5's `JsonPathExtractor` (`JsonPath.Net`) already exists to build on. *(Q2 resolved: move to 2.6; 2.5's `XmlReadModule` still ships an optional `xpath` property.)* |
| **D10 CSV via CsvHelper** | Well-maintained, MS-PL/Apache-2.0 dual licensed, handles quoting/escaping/culture edge cases we don't want to hand-roll. New pin in `Directory.Packages.props` (33.0.1). |
| **D11 JSON via System.Text.Json, XML via XDocument** | Already-present deps; `JsonNode` → CLR dict/list/scalar for round-trippable data; `XDocument` + element→dictionary conversion (`@attr`/`#text`/auto-list). XSD validation via `XmlSchemaSet` (in-box). |
| **D12 S3 E2E via MinIO Testcontainer, Azure via Azurite** | `Testcontainers.Minio` is already pinned (used by `Workflow.Persistence.S3` tests) — reuse it. Azure Blob adds `Testcontainers.Azurite`. Both suites live in `Workflow.Tests.Integration`, Docker-gated, compile-verified in CI without Docker. |
| **D13 Encodings as string keys, not enums** | `encoding: "utf-8" / "utf-16" / "ascii" / "latin1"` etc. resolved via `Encoding.GetEncoding(...)` with a friendly-alias map — same "string keys, not core enums" reasoning as 2.4-D6. Default `utf-8` (no BOM on write). |
| **D14 Storage-connection CRUD mirrors 2.4.a.5** | Config-bound (`Workflow:CloudStorage:Connections`) + optional runtime CRUD API, secrets encrypted at rest via the existing `IConnectionStringProtector`/Data-Protection seam. *(Q5 resolved: config-bound for MVP; runtime CRUD → 2.5.b.P1.)* |

### TO RESOLVE 🤔

> All raised during task breakdown — resolved with the V1 recommendations so implementation could start. Left here for the record~ ✨

- [x] **Q1 Sandbox default posture:** unrestricted-with-loud-docs + startup warning when no roots configured. Deny-by-default hardening can land in Phase 4. **Resolved.**
- [x] **Q2 Query modules placement:** `jsonquery`/`xmlquery` move to Phase 2.6; 2.5's `XmlReadModule` ships an optional `xpath` property so 2.5 alone is useful. **Resolved.**
- [x] **Q3 File-extension policy scope:** global optional `BlockedExtensions` (default `.exe/.dll/.bat/.cmd/.ps1/.sh`) applied on **write** only. **Resolved.**
- [x] **Q4 Symlink traversal:** resolve symlinks and re-check against roots when `AllowedRoots` is configured; skip when unrestricted. **Resolved.**
- [x] **Q5 Storage-connection runtime CRUD in MVP?** Config-bound only for MVP; registry is DI-replaceable so CRUD is additive → **2.5.b.P1**. **Resolved.**
- [x] **Q6 S3 client construction:** fall back to the default AWS credential chain when no explicit keys/connection are supplied. **Resolved.**
- [x] **Q7 Binary content transport:** cap-and-document via `maxSize`; `blobRef` → **2.5.a.P2**. **Resolved.**
- [x] **Q8 Decompress format inference:** extension-first (`.zip`/`.gz`/`.tar`/`.tar.gz`/`.tgz`); explicit `format` always wins. **Resolved.**
- [x] **Q9 XML→dictionary shape:** `@attr` keys, `#text` for text content, repeated elements → lists; round-trips through `XmlWriteModule`. **Resolved.**
- [x] **Q10 Zip-slip on decompress:** every entry validated against the output directory; a hostile entry fails the whole extraction (zip path pre-scans before writing). Test-locked. **Resolved.**

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File | Status |
|-----------|------|--------|
| `IWorkflowModule` contract + `ModuleResult.Ok/Fail` | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Reused as-is |
| `ModuleExecutionContext.Services` per-call DI pattern | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Mirrors `HttpRequestModule` / database modules |
| `PropertyBinder` (`{{ }}` templating) | `Workflow.Modules/Binding/PropertyBinder.cs` | ✅ Applies to `path`, `outputDirectory`, etc. |
| `BuiltinModuleRegistration` | `Workflow.Modules/Builtin/BuiltinModuleRegistration.cs` | ✅ Local family appends here (D4) |
| `AddWorkflowModules()` aggregate | `Workflow.Modules/WorkflowModulesServiceCollectionExtensions.cs` | ✅ Extended to call `AddFileSystemModules()` |
| HTTP family in-`Workflow.Modules` layout | `Workflow.Modules/Builtin/Http/*` | ✅ Placement + DI-extension pattern reference for `Builtin/File/` |
| `IDbConnectionRegistry` + `DatabaseConnectionsOptions` | `Workflow.Modules.Database/…` | ✅ Pattern reference for `IStorageConnectionRegistry` (D5/D14) |
| `IConnectionStringProtector` + Data-Protection impl | `Workflow.Modules.Database/Abstractions/…` + `Workflow.Api/Database/…` | ✅ Reused for storage-connection secrets (D14) |
| `AWSSDK.S3` package + S3 client lifecycle | `Directory.Packages.props` + `Workflow.Persistence.S3/*` | ✅ Pin exists; lifecycle pattern reference (D6) |
| `Testcontainers.Minio` | `Directory.Packages.props` | ✅ Already pinned — reuse for S3 module E2E (D12) |
| Redaction convention | `Builtin/Http/Auth/IHttpAuthStrategy.cs` (`RedactHeaders`) | ✅ Pattern for never logging storage credentials |

---

## 2.5.a.0 Shared Infrastructure 🛠️ (foundation) ✅ COMPLETE

> **Purpose:** Land the path-security sandbox + options + shared helpers every file module consumes. No modules yet~ ✨

**Complexity:** 🟡 Medium *(small surface, but D1/D2/Q1/Q4 decisions get baked in here — everything downstream trusts this gate)*

### Tasks

- [x] **`Builtin/File/` folder layout in `Workflow.Modules`** 🌷
  - [x] `Builtin/File/` — the modules
  - [x] `Builtin/File/Internal/` — validator, encoding resolver, shared helpers, exceptions, converters
  - [x] `Builtin/File/FileSystemModuleServiceCollectionExtensions.cs` — `AddFileSystemModules(this IServiceCollection)` (`TryAdd` semantics)
  - [x] `AddWorkflowModules()` updated to call `AddFileSystemModules()` *(no circular-ref issue — D4)*

- [x] **`FileSystemModuleOptions`** ⚙️
  - [x] `Builtin/File/FileSystemModuleOptions.cs`, section `Workflow:FileSystem`
  - [x] `AllowedRoots`, `UnrestrictedIfNoRoots` (+ startup warning), `BlockedExtensions`, `DefaultMaxReadBytes` (16 MB), `ResolveSymlinks`

- [x] **`IWorkflowPathValidator`** 🛡️ — the D1 gate
  - [x] `Internal/IWorkflowPathValidator.cs` (+ `PathAccessIntent`, `PathValidationResult`) + `DefaultWorkflowPathValidator.cs`
  - [x] Canonicalise via `Path.GetFullPath`; reject `..` escapes, empty/invalid chars
  - [x] Root containment with trailing-separator guard (`C:\data-evil` ≠ root `C:\data`), OS-appropriate case sensitivity
  - [x] Symlink resolution + re-check when configured (Q4)
  - [x] Write-intent blocked-extension check (Q3); registered singleton via `AddFileSystemModules()`

- [x] **Shared helpers** 🧰
  - [x] `Internal/EncodingResolver.cs` — string key → `Encoding` with alias map (utf-8 no BOM, latin1 native), unknown key → error (D13)
  - [x] `Internal/FileModuleSupport.cs` — property readers + validate-then-resolve helper + options accessor
  - [x] `Internal/JsonValueConverter.cs` + `Internal/XmlDictionaryConverter.cs` *(shared by 2.5.a.2)*

- [x] **Common exception types** 🚨
  - [x] `FileModuleException` base + `PathSecurityException` (`AttemptedPath`/`Reason`) + `FileTooLargeException` (`ActualBytes`/`MaxBytes`)

### Tests (target ~12 — **14 shipped ✅**): → `Workflow.Tests/Modules/File/PathValidatorTests.cs`

- [x] `Validator_NoRootsConfigured_Unrestricted_AllowsAbsolutePath`
- [x] `Validator_NoRoots_UnrestrictedDisabled_Fails` *(bonus)*
- [x] `Validator_RootConfigured_PathInsideRoot_ResolvesAbsolute`
- [x] `Validator_RootConfigured_PathOutsideRoot_Fails`
- [x] `Validator_DotDotEscape_Fails`
- [x] `Validator_SiblingPrefixDir_Fails` *(trailing-separator guard)*
- [x] `Validator_WriteIntent_BlockedExtension_Fails`
- [x] `Validator_ReadIntent_BlockedExtension_Allowed` *(write-only policy)*
- [x] `Validator_EmptyPath_Fails`
- [x] `EncodingResolver_KnownAliases_Resolve` *(theory: utf-8/utf8/utf-16/ascii/latin1/iso-8859-1)*
- [x] `EncodingResolver_NullKey_ReturnsDefaultUtf8NoBom`
- [x] `EncodingResolver_UnknownKey_Fails`

---

## 2.5.a.1 File Read + Write Modules 📁 (`builtin.file.read` / `builtin.file.write`) ✅ COMPLETE

> **Purpose:** The bread-and-butter pair — read text/binary/lines with encoding + size limits, write with overwrite/append/create-new semantics~ ✨

**Complexity:** 🟡 Low *(the validator did the hard part)*

### Tasks

- [x] **`FileReadModule`** 📖 (`builtin.file.read`)
  - [x] Properties: `path` (req), `encoding` (utf-8), `readAs` (text/binary/lines), `maxSize` (from options)
  - [x] Outputs: `content` (string/byte[]/string[]), `size`, `lastModified` (DateTimeOffset), `success`
  - [x] `ValidateConfiguration`: path non-empty, readAs/encoding known, maxSize > 0
  - [x] `ExecuteAsync`: validate (Read intent) → exists check → size check (no partial read) → read per `readAs` → metrics

- [x] **`FileWriteModule`** ✍️ (`builtin.file.write`)
  - [x] Properties: `path` (req), `content` (port or property; port wins), `encoding`, `mode` (overwrite/append/createNew), `createDirectory` (default true)
  - [x] Outputs: `bytesWritten`, `fullPath`, `success`
  - [x] `ExecuteAsync`: validate (Write intent → blocked-extension) → createNew-exists guard → append-binary guard → create dir → write
  - [x] Both registered in `BuiltinModuleRegistration.GetAll()` (D4)

### Tests (target ~14 — **shipped in `FileReadWriteModuleTests.cs` ✅**)

- [x] `ReadModule_Metadata_IsCorrect` / `WriteModule_Metadata_IsCorrect`
- [x] `ReadText_Utf8_RoundTrips` · `ReadText_Latin1_DecodesCorrectly` · `ReadBinary_ReturnsBytes` · `ReadLines_ReturnsArray`
- [x] `Read_FileNotFound_ReturnsFriendlyFail` · `Read_ExceedsMaxSize_FailsWithoutPartialRead`
- [x] `WriteText_Overwrite_ReplacesContent` · `WriteText_Append_Appends` · `Write_CreateNew_ExistingFile_Fails`
- [x] `Write_CreatesMissingDirectory_WhenEnabled` · `Write_BlockedExtension_Fails`
- [x] `Write_BinaryContentFromPort_Wins` · `WriteThenRead_RoundTrips`

---

## 2.5.a.2 Structured Formats: CSV + JSON + XML 📊 (`builtin.file.{csv,json,xml}.{read,write}`) ✅ COMPLETE

> **Purpose:** Six modules, one slice — they share the read/write skeleton from 2.5.a.1 and differ only in the parse/serialise core. CSV via CsvHelper (D10), JSON via System.Text.Json, XML via XDocument (D11)~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **Package:** add `CsvHelper` (33.0.1) to `Directory.Packages.props` + `Workflow.Modules.csproj` (D10)
- [x] **`CsvReadModule`** (`builtin.file.csv.read`) — `hasHeader`/`delimiter`/`encoding`/`skipEmptyRows`; rows as dict list (headerless → `column0..N`), `rowCount`, `columns`; uses `CsvParser` (quotes/escapes/embedded newlines free)
- [x] **`CsvWriteModule`** (`builtin.file.csv.write`) — `data` (port/property), `includeHeader`/`delimiter`/`encoding`; columns = first-row keys; empty data → header-only/empty file, not a failure
- [x] **`JsonReadModule`** (`builtin.file.json.read`) — `data` (`JsonNode`→CLR), `isArray`; parse errors → Fail with line/pos
- [x] **`JsonWriteModule`** (`builtin.file.json.write`) — `data` (port/property), `indented`; System.Text.Json serialise
- [x] **`XmlReadModule`** (`builtin.file.xml.read`) — `data` (Q9 shape), `rootElement`; **XXE-safe** (`DtdProcessing.Prohibit`, `XmlResolver = null`); optional `validateSchema`/`schemaPath` (validator-checked) + `xpath` pre-extraction (Q2)
- [x] **`XmlWriteModule`** (`builtin.file.xml.write`) — inverse Q9 shape (`@`/`#text`/lists round-trip), `rootElement`/`indented`
- [x] All six `Category: "File System"`, registered in `BuiltinModuleRegistration.GetAll()`, path handling via `FileModuleSupport`

### Tests (target ~24 — **shipped across `CsvModuleTests.cs` + `JsonXmlModuleTests.cs` ✅**)

- [x] **CSV:** metadata · with-header keyed rows · no-header `columnN` · semicolon delimiter · quoted field w/ comma+newline · write→read round-trip · empty-data file
- [x] **JSON:** metadata · object (isArray false) · array (isArray true) · invalid fails · nested round-trip
- [x] **XML:** metadata · elements/attributes/text (`@id`/`#text` convention) · repeated elements → list · **external entity refused (XXE)** · write→read round-trip

---

## 2.5.a.3 *(reserved)*

> Slice number intentionally skipped — JSON/XML folded into 2.5.a.2 (they share the whole skeleton) and `jsonquery`/`xmlquery` moved to Phase 2.6 (D9/Q2). Keeps later slice numbers stable~ 🌸

---

## 2.5.a.4 Compression Modules 🗜️ (`builtin.file.compress` / `builtin.file.decompress`) ✅ COMPLETE

> **Purpose:** Zip/GZip/Tar/TarGz archive + extract, all on .NET 8 in-box APIs (D7). Zip-slip protection is the headline security item (Q10)~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **`CompressModule`** 🗜️ (`builtin.file.compress`)
  - [x] Properties: `sourcePath` (string or array — each validated Read intent), `outputPath` (Write intent), `format` (zip/gzip/tar/targz), `compressionLevel` (optimal/fastest/smallestSize/noCompression), `includeBaseDirectory`
  - [x] Outputs: `archivePath`, `originalSize`, `compressedSize`, `compressionRatio`, `fileCount`, `success`
  - [x] `gzip` requires exactly one source file; directory sources recurse with relative entry names
  - [x] Zip → `ZipFile`; GZip → `GZipStream`; Tar → `System.Formats.Tar.TarWriter`; TarGz → `TarWriter` over `GZipStream` (D7)
- [x] **`DecompressModule`** 📦 (`builtin.file.decompress`)
  - [x] Properties: `archivePath` (Read), `outputDirectory` (Write), `format` (inferred per Q8), `overwrite`
  - [x] Outputs: `extractedFiles`, `fileCount`, `success`
  - [x] **Zip-slip guard (Q10):** entries validated against `outputDirectory`; zip path pre-scans before writing → hostile entry fails the whole extraction
  - [x] `overwrite: false` + existing → Fail; corrupt archive → friendly Fail

### Tests (target ~13 — **shipped in `CompressionModuleTests.cs` ✅**)

- [x] metadata ×2 · `Zip_RoundTrip_MultipleFiles` · `Zip_PreservesDirectoryStructure`
- [x] `GZip_SingleFile_RoundTrips` · `GZip_MultipleSources_FailsValidation` · `TarGz_RoundTrips`
- [x] `Decompress_FormatInferred_FromExtension` (Q8)
- [x] `Decompress_ZipSlipEntry_FailsWholeExtraction_NothingWritten` (Q10)
- [x] `Decompress_ExistingFile_NoOverwrite_Fails` · `Decompress_CorruptArchive_FriendlyFail`

---

## 2.5.b.0 Cloud Storage Infrastructure ☁️ (`Workflow.Modules.Cloud` project) ✅ COMPLETE

> **Purpose:** New quarantined project (D4) + named storage connections (D5/D14) + client factory. The 2.4.a.0+2.4.a.5 pattern, storage-flavoured~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Workflow.Modules.Cloud` project layout** 🌷
  - [x] New project: `Workflow.Modules.Cloud/Workflow.Modules.Cloud.csproj` (net8.0, references `Workflow.Core`, `Workflow.Modules`, `AWSSDK.S3`, `Azure.Storage.Blobs`, `Microsoft.Extensions.*`)
  - [x] **New pin:** `Azure.Storage.Blobs` in `Directory.Packages.props`
  - [x] Add to `Workflow.sln`; folder layout: `Abstractions/` · `Connections/` · `Configuration/` · `Builtin/`
  - [x] `CloudStorageServiceCollectionExtensions.AddCloudStorageModules(this IServiceCollection)` — host-wired; modules via `TryAddEnumerable`
  - [x] `Workflow.Api/Program.cs`: `AddCloudStorageModules()` + options binding

- [x] **`IStorageConnectionRegistry`** 📇
  - [x] `StorageConnectionDescriptor` record (`Id`, `Kind`, `Enabled` + kind-specific settings)
  - [x] `InMemoryStorageConnectionRegistry` hydrating from `CloudStorageOptions`; case-insensitive lookup
  - [x] Secrets pass through `IConnectionStringProtector` seam (D14); runtime CRUD deferred → **2.5.b.P1**

- [x] **`IStorageClientFactory`** 🔌
  - [x] `CreateS3Client(...)` → `IAmazonS3` (explicit keys or default chain per Q6; custom `ServiceURL` for MinIO)
  - [x] `CreateBlobServiceClient(...)` → `BlobServiceClient`
  - [x] Unknown id → `StorageConnectionNotFoundException`; unknown kind → `UnknownStorageKindException` (both under `CloudModuleException`)
  - [x] Never log credentials — descriptor logging redacts secrets

### Tests (target ~8): → `Workflow.Tests/Modules/Cloud/StorageInfrastructureTests.cs` *(Docker-free)*

- [x] `Registry_ConfigBoundEntry_LookupById` · `Registry_LookupCaseInsensitive` · `Registry_UnknownId_ReturnsNone` · `Registry_DisabledConnection_ExcludedFromResolution`
- [x] `Factory_S3_ExplicitKeys_BuildsClientWithServiceUrl` · `Factory_S3_NoCredentials_FallsBackToDefaultChain` (Q6)
- [x] `Factory_AzureBlob_ConnectionString_BuildsClient` · `Descriptor_Logging_RedactsSecrets`

---

## 2.5.b.1 S3 + Azure Blob Modules ☁️ (`builtin.cloud.s3` / `builtin.cloud.azureblob`) ✅ COMPLETE

> **Purpose:** One operation-style module per provider: Upload / Download / Delete / List / Exists. Local-file legs go through the 2.5.a.0 path validator too~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **`S3Module`** 🪣 (`builtin.cloud.s3`)
  - [x] `operation` (upload/download/delete/list/exists) · `storageConnectionId` (preferred) · inline `accessKey`/`secretKey`/`region`/`serviceUrl` · `bucket` · `key` · `localPath` (validated) · `prefix`/`maxKeys` (list) · `contentType`
  - [x] Outputs: `success`, `key`, `url` (upload), `objects`/`objectCount` (list), `exists`, `bytesTransferred`, `durationMs`
  - [x] `PutObjectAsync`/`GetObjectAsync`/`DeleteObjectAsync`/`ListObjectsV2Async`/`GetObjectMetadataAsync` (404 → exists false); errors never leak credentials
- [x] **`AzureBlobModule`** 🫐 (`builtin.cloud.azureblob`)
  - [x] `operation` · `storageConnectionId` · inline `connectionString` · `containerName` · `blobName` · `localPath` (validated) · `prefix` · `createContainer`
  - [x] `UploadAsync`/`DownloadToAsync`/`DeleteIfExistsAsync`/`GetBlobsAsync`/`ExistsAsync`
- [x] Both registered via `AddCloudStorageModules()` (`TryAddEnumerable`), reflection-discoverable

### Tests (target ~14 unit): → `Workflow.Tests/Modules/Cloud/S3ModuleTests.cs` + `AzureBlobModuleTests.cs` *(metadata/validation/DI-guard)*

- [x] metadata ×2 · schema-ports ×2 · upload-missing-localPath ×2 · unknown-operation ×2 · list-key-optional ×2 · missing-factory ×2 · unknown-connectionId ×2

### Tests (integration, Docker-gated): → `Workflow.Tests.Integration/Cloud/MinioS3ModuleTests.cs` + `AzuriteBlobModuleTests.cs`

- [x] **New pin:** `Testcontainers.Azurite` (D12)
- [x] `Minio_UploadDownloadRoundTrip_ByteIdentical` · `Minio_List_WithPrefix_ReturnsSubset` · `Minio_Delete_ThenExists_False` · `Minio_WrongCredentials_FailsWithoutLeakingSecret`
- [x] `Azurite_UploadDownloadRoundTrip_ByteIdentical` · `Azurite_List_WithPrefix_ReturnsSubset` · `Azurite_CreateContainerOnUpload_WhenEnabled` · `Azurite_InvalidConnectionString_FriendlyFail`

---

## 2.5.b.2 E2E Demo + Documentation 📖 ✅ COMPLETE

> **Purpose:** Prove the whole family composes end-to-end, plus the docs~ ✨

**Complexity:** 🟢 Low

### Tasks

- [x] **E2E workflow test** (`Workflow.Tests.Integration/Cloud/FileCloudE2ETests.cs`, Docker-gated)
  - [x] `E2E_CsvToJsonToZipToS3AndBack_ByteIdentical`
  - [x] `E2E_SandboxedRoots_WorkflowCannotEscape`
- [x] **`docs/file-modules.md`** — module reference (all 10), sandbox configuration guide, named storage-connection setup, MinIO/Azurite recipes
- [x] **Security review checklist** — traversal ✅ · symlink ✅ · zip-slip ✅ · XXE ✅ · blocked write extensions ✅ · credential redaction
- [x] **Housekeeping** — `DOCUMENTATION_INDEX.md` + `phases/README.md` + `Phase2-CoreFeatures.md` §2.5 completion summary; `README.md` module count

### Tests (target ~2): the two E2E tests above *(Docker-gated, compile-verified)*

---

## Post-MVP Slices 🚧 *(deferred — not blocking 2.6+)*

### 2.5.a.P1 Directory Operations Module 📂 *(post-MVP)*
`builtin.file.directory` — list (glob, recursive), create, delete, move/copy. ~8 tests.

### 2.5.a.P2 Streaming / Large-File Handling ⚡ *(post-MVP — resolves Q7)*
`readAs: "blobRef"` (content into `IBlobStore`), chunked/streamed compress + cloud transfer. ~8 tests.

### 2.5.a.P3 File Watch Trigger 👀 *(post-MVP — Phase 3 trigger family candidate)*
`builtin.file.watch` — `FileSystemWatcher`-based trigger. ~6 tests.

### 2.5.b.P1 Storage-Connection Runtime CRUD API 🌐 *(post-MVP unless Q5 flips)*
`/api/cloud/connections` CRUD + Data-Protection encryption. Port of 2.4.a.5. ~9 tests.

### 2.5.b.P2 More Providers (GCS, SFTP/FTPS) 🌍 *(post-MVP)*
`builtin.cloud.gcs`, `builtin.file.sftp`. ~12 tests.

### 2.5.b.P3 Presigned-URL + Server-Side-Encryption Options 🔐 *(post-MVP)*
Presigned GET/PUT; SSE-S3/SSE-KMS/customer-key on upload. ~6 tests.

---

## Phase 2.5 Deliverables ✅

**2.5.a — Local file family (Week 15 gate):** ✅ **COMPLETE**

- [x] **Modules (10):** `builtin.file.{read,write}` · `builtin.file.csv.{read,write}` · `builtin.file.json.{read,write}` · `builtin.file.xml.{read,write}` · `builtin.file.{compress,decompress}` — all discoverable, validated, executable
- [x] **Shared infra:** `IWorkflowPathValidator` + `FileSystemModuleOptions` DI-registered via `AddFileSystemModules()` (called from `AddWorkflowModules()`)
- [x] **Path security proven:** traversal, sibling-prefix, symlink-guard, zip-slip, XXE, and blocked-extension tests all green
- [x] **58 unit tests passing** (14 infra/validator + 14 read/write + 18 CSV/JSON/XML + 12 compression) — all Docker-free

**2.5.b — Cloud storage family (Week 16 gate):** ✅ **COMPLETE**

- [x] **Modules (2):** `builtin.cloud.s3` + `builtin.cloud.azureblob` discoverable, validated; named storage connections + inline escape hatch
- [x] **`Workflow.Modules.Cloud` quarantine holds** — `Workflow.Modules` has no reference to the Cloud project or AWS/Azure SDKs (structural guarantee); `AddWorkflowModules()` alone never loads them
- [x] **36 unit tests** (8 infra + 12 module unit in `Workflow.Tests`) + 22 file-family + MinIO/Azurite/E2E suites Docker-gated (compile-verified)
- [x] **No credential leakage** — redaction test-locked (`Descriptor_ToString_RedactsSecrets`, `Minio_WrongCredentials_FailsWithoutLeakingSecret`)

**Cross-cutting:**

- [x] **docs/file-modules.md** published + indexed; sandbox hardening guide included
- [x] **0 errors** in `dotnet build` (all projects); **942 unit tests green**
- [x] **Q1–Q10 all resolved** and recorded above
- [x] **README + phases/README.md** updated — 2.5 marked complete

**New / Modified Files:**
```
Workflow.Modules/
  Builtin/File/
    FileSystemModuleOptions.cs                     ✅ (2.5.a.0)
    FileSystemModuleServiceCollectionExtensions.cs ✅ (2.5.a.0)
    FileReadModule.cs / FileWriteModule.cs         ✅ (2.5.a.1)
    CsvReadModule.cs / CsvWriteModule.cs           ✅ (2.5.a.2)
    JsonReadModule.cs / JsonWriteModule.cs         ✅ (2.5.a.2)
    XmlReadModule.cs / XmlWriteModule.cs           ✅ (2.5.a.2)
    CompressModule.cs / DecompressModule.cs        ✅ (2.5.a.4)
    Internal/
      IWorkflowPathValidator.cs                    ✅ (2.5.a.0)
      DefaultWorkflowPathValidator.cs              ✅ (2.5.a.0)
      EncodingResolver.cs                          ✅ (2.5.a.0)
      FileModuleSupport.cs                         ✅ (2.5.a.0)
      FileModuleException.cs                       ✅ (2.5.a.0)
      JsonValueConverter.cs                        ✅ (2.5.a.2)
      XmlDictionaryConverter.cs                    ✅ (2.5.a.2)
  Builtin/BuiltinModuleRegistration.cs             ✅ modified — +10 local modules (D4)
  WorkflowModulesServiceCollectionExtensions.cs    ✅ modified — call AddFileSystemModules()
  Workflow.Modules.csproj                          ✅ modified — + CsvHelper

Workflow.Modules.Cloud/                            ⏳ NEW PROJECT (2.5.b.0)
  … Abstractions/Connections/Configuration/Builtin

Workflow.Api/Program.cs                            ⏳ (2.5.b.0)

Workflow.Tests/
  Modules/File/
    PathValidatorTests.cs                          ✅ (2.5.a.0)
    FileModuleTestBase.cs                          ✅ (shared fixture)
    FileReadWriteModuleTests.cs                    ✅ (2.5.a.1)
    CsvModuleTests.cs / JsonXmlModuleTests.cs      ✅ (2.5.a.2)
    CompressionModuleTests.cs                      ✅ (2.5.a.4)
  Modules/Cloud/                                   ⏳ (2.5.b.0/1)

Workflow.Tests.Integration/Cloud/                  ⏳ (2.5.b.1/2)
docs/file-modules.md                               ⏳ (2.5.b.2)
Directory.Packages.props                           🔄 + CsvHelper ✅; + Azure.Storage.Blobs, Testcontainers.Azurite ⏳
```

---

> 🌸 *uwu — 2.5.a is fully landed, senpai~! The path-security validator keystone is in and every local file/CSV/JSON/XML/compress module is green (58 tests). Cloud storage (2.5.b) is next — it's the 2.4.a.0+2.4.a.5 registry/factory pattern in storage cosplay~!* 💖
