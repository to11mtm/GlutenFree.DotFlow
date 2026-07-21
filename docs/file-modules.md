# File System & Cloud Storage Modules 📁☁️

*Phase 2.5 — Made with 💖 by Ami-Chan! UwU ✨*

DotFlow's **file family** reads and writes local files, parses structured formats (CSV/JSON/XML),
and compresses/decompresses archives — plus a **cloud-storage family** for Amazon S3 and Azure Blob
Storage. Every path a module touches passes through one shared security gate
(`IWorkflowPathValidator`) so a workflow can never wander outside its sandbox~ 🌷

---

## 🛡️ The path-security sandbox (read this first)

All file modules resolve `IWorkflowPathValidator` and validate the path **before** any I/O:

- Paths are canonicalised (`Path.GetFullPath`) and traversal (`..` escaping a root) is rejected.
- When **`AllowedRoots`** is configured, a path must resolve inside one of the roots
  (sibling-prefix attacks like `C:\data-evil` vs root `C:\data` are blocked by a trailing-separator
  guard). Symlinks are resolved and re-checked against the roots.
- On **write**, a blocked-extension policy applies (`.exe`, `.dll`, `.bat`, `.cmd`, `.ps1`, `.sh` by default).
- Archive extraction validates every entry against the output directory (**zip-slip** protection).
- XML parsing prohibits DTDs and disables the external resolver (**XXE** protection).

### Configuration (`appsettings.json`)

```jsonc
{
  "Workflow": {
    "FileSystem": {
      "AllowedRoots": [ "/srv/workflow-files" ], // empty = unrestricted (dev only)
      "UnrestrictedIfNoRoots": true,             // set false to hard-deny when no roots
      "BlockedExtensions": [ ".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh" ],
      "DefaultMaxReadBytes": 16777216,           // 16 MiB
      "ResolveSymlinks": true
    }
  }
}
```

> ⚠️ **Production hardening:** leave `AllowedRoots` empty only for local dev. When empty and
> `UnrestrictedIfNoRoots` is `true`, a startup warning is logged. Configure roots in production.

The family is registered automatically by `services.AddWorkflowModules()`.

---

## 📁 Local file modules

| Module ID | Purpose | Key properties |
|-----------|---------|----------------|
| `builtin.file.read` | Read a file as text/binary/lines | `path`, `encoding`, `readAs`, `maxSize` |
| `builtin.file.write` | Write text/binary content | `path`, `content`, `encoding`, `mode`, `createDirectory` |
| `builtin.file.csv.read` | Parse a CSV into row dictionaries | `path`, `hasHeader`, `delimiter`, `encoding`, `skipEmptyRows` |
| `builtin.file.csv.write` | Write row dictionaries to CSV | `path`, `data`, `includeHeader`, `delimiter`, `encoding` |
| `builtin.file.json.read` | Parse JSON to an object graph | `path`, `encoding` |
| `builtin.file.json.write` | Serialise an object graph to JSON | `path`, `data`, `indented`, `encoding` |
| `builtin.file.xml.read` | Parse XML to a dictionary graph | `path`, `encoding`, `validateSchema`, `schemaPath`, `xpath` |
| `builtin.file.xml.write` | Write a dictionary graph to XML | `path`, `data`, `rootElement`, `indented`, `encoding` |
| `builtin.file.compress` | Create a Zip/GZip/Tar/TarGz archive | `sourcePath`, `outputPath`, `format`, `compressionLevel`, `includeBaseDirectory` |
| `builtin.file.decompress` | Extract an archive (zip-slip safe) | `archivePath`, `outputDirectory`, `format`, `overwrite` |

### Notes

- **Encodings** are friendly string keys: `utf-8` (default, no BOM), `utf-16`, `ascii`, `latin1`.
- **`readAs`**: `text` → `string`, `binary` → `byte[]`, `lines` → `string[]`. `maxSize` caps reads
  (exceeding it fails cleanly — no partial read).
- **CSV** values surface as strings (type inference is a Phase 2.6 concern); headerless files get
  `column0..N` keys.
- **XML ↔ dictionary** convention: attributes → `@name` keys, element text → `#text`, repeated
  sibling elements → lists. This round-trips through `xml.write`.
- **Compression** uses .NET in-box APIs only (`System.IO.Compression` + `System.Formats.Tar`).
  `gzip` handles a single file; use `tar`/`targz` for multiple.

---

## ☁️ Cloud storage modules

Quarantined in the `Workflow.Modules.Cloud` project (AWS + Azure SDKs) and wired by the host via
`services.AddCloudStorageModules()` — SDK-free deployments don't pay for it.

| Module ID | Provider | Operations |
|-----------|----------|------------|
| `builtin.cloud.s3` | Amazon S3 (and S3-compatible: MinIO, …) | `upload`, `download`, `delete`, `list`, `exists` |
| `builtin.cloud.azureblob` | Azure Blob Storage | `upload`, `download`, `delete`, `list`, `exists` |

### Named storage connections (preferred)

Credentials live in configuration, never in workflow definitions. Modules reference a
`storageConnectionId`:

```jsonc
{
  "Workflow": {
    "CloudStorage": {
      "Connections": [
        { "Id": "prod-s3", "Kind": "s3", "Region": "us-west-2" },      // creds via AWS chain
        { "Id": "minio", "Kind": "s3", "AccessKey": "…", "SecretKey": "…", "ServiceUrl": "http://minio:9000" },
        { "Id": "prod-blob", "Kind": "azureBlob", "ConnectionString": "…" }
      ]
    }
  }
}
```

- **S3** without explicit keys falls back to the **default AWS credential chain** (env vars, instance
  profiles) — the expected behaviour on EC2/ECS.
- Inline credentials (`accessKey`/`secretKey`/`connectionString` on the module) are a documented
  **dev-only escape hatch**.
- The module's `localPath` (for `upload`/`download`) goes through the same path validator.
- Credentials are never written to logs or error messages.

> Runtime CRUD for storage connections (encrypted at rest) is tracked as post-MVP slice **2.5.b.P1**.

---

## 🧪 Local development (MinIO + Azurite)

Integration tests spin up MinIO (S3) and Azurite (Azure Blob) via Testcontainers and are Docker-gated
(`[Trait("Category", "Integration")]`). To run them locally:

```bash
# requires a running Docker daemon
dotnet test Workflow.Tests.Integration --filter "Category=Integration"
```

The Docker-free unit suite (`Workflow.Tests/Modules/File` + `Workflow.Tests/Modules/Cloud`) covers
metadata, validation, path security, and format round-trips without any containers.

---

## 🔒 Security checklist

| Threat | Mitigation | Test |
|--------|-----------|------|
| Directory traversal | canonical-prefix root check | `Validator_DotDotEscape_Fails` |
| Sibling-prefix escape | trailing-separator guard | `Validator_SiblingPrefixDir_Fails` |
| Symlink escape | resolve + re-check target | `Validator_Symlink_EscapingRoot_Fails` |
| Zip-slip | per-entry validation + pre-scan | `Decompress_ZipSlipEntry_FailsWholeExtraction_NothingWritten` |
| XXE | DTD prohibited, resolver disabled | `ReadXml_ExternalEntity_Refused` |
| Malicious writes | write-side blocked extensions | `Write_BlockedExtension_Fails` |
| Credential leakage | redaction in descriptor/errors | `Descriptor_ToString_RedactsSecrets` |
