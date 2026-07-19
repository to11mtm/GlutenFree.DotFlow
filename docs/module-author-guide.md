# 📦 Module Author Guide — Shipping Modules for DotFlow

*Made with 💖 by Ami-Chan! UwU* ✨

This guide covers everything you need to know to create, package, and ship a custom workflow module that can be dynamically loaded by DotFlow at runtime~ 🎀

---

## 🌸 Quick Start

A DotFlow module is a .NET class library that implements `IWorkflowModule`. The engine discovers, validates, and loads it from disk at runtime using an isolated `AssemblyLoadContext` — just like a VS Code extension or a Roslyn analyzer~ ✨

---

## 1. Create Your Module Project

```bash
dotnet new classlib -n MyCompany.DotFlow.MyModule -f net8.0
cd MyCompany.DotFlow.MyModule
dotnet add package GlutenFree.DotFlow.Modules  # (future NuGet package)
```

Or reference the projects directly during development:

```xml
<ItemGroup>
  <PackageReference Include="GlutenFree.DotFlow.Core" Version="1.0.0" />
  <PackageReference Include="GlutenFree.DotFlow.Modules" Version="1.0.0" />
</ItemGroup>
```

> **CopilotNote:** Only reference `Workflow.Core` and `Workflow.Modules` — NOT `Workflow.Engine`
> or `Workflow.Api`. Those are host-side concerns~ 💖

---

## 2. Implement IWorkflowModule

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

public sealed class MyHttpModule : IWorkflowModule
{
    private readonly HttpClient _httpClient;

    // Constructor injection via DI is supported!
    public MyHttpModule(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string ModuleId    => "mycompany.http.get";
    public string DisplayName => "HTTP GET";
    public string Category    => "HTTP";
    public string Description => "Performs an HTTP GET request and returns the response body.";
    public string Icon        => "🌐";
    public Version Version    => new(1, 0, 0);

    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("url", "URL", typeof(string),
                Description: "The URL to GET.", IsRequired: true)),
        Outputs: Arr.create(
            new PortDefinition("body", "Response Body", typeof(string),
                Description: "The response body text.", IsRequired: false)),
        Properties: Arr<ModulePropertyDefinition>.Empty);

    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var url = context.Inputs["url"]?.ToString()
            ?? throw new ArgumentNullException("url");

        var response = await _httpClient.GetStringAsync(url, cancellationToken);

        return ModuleResult.Ok(new Dictionary<string, object?> { ["body"] = response });
    }
}
```

### Module ID Naming Convention

Module IDs must match `^[a-z][a-z0-9._-]*$` — the `ModuleValidator` will reject anything else:

| ✅ Valid | ❌ Invalid |
|---|---|
| `mycompany.http.get` | `MyCompany.Http.Get` (uppercase) |
| `acme.db.query-v2` | `acme db query` (spaces) |
| `builtin.log` | `1.log` (starts with digit) |

---

## 3. Declare NuGet Dependencies Correctly

This is the **most important section** for module authors shipping third-party packages~ 🎯

### How dependency resolution works

When DotFlow loads your assembly via `AssemblyModuleLoader`, it uses an `AssemblyDependencyResolver`
which reads your `.deps.json` file to locate your dependencies on disk:

```
plugins/
  MyCompany.DotFlow.MyModule/
    MyCompany.DotFlow.MyModule.dll        <- your module
    MyCompany.DotFlow.MyModule.deps.json  <- REQUIRED: dependency manifest
    Newtonsoft.Json.dll                   <- your private dependency copy
```

The `.deps.json` is **automatically generated** by the .NET SDK when you build — you just need to
make sure you ship it alongside your DLL~ ✨

### What gets shared vs. isolated

| Assembly | Behavior | Why |
|---|---|---|
| `Workflow.Core.dll` | **Shared** (host's copy) | Type-identity — `IWorkflowModule` must be the same type |
| `Workflow.Modules.dll` | **Shared** (host's copy) | Same reason |
| `Microsoft.Extensions.*.dll` | **Shared** (host's copy) | Already in host, reused |
| `System.Net.Http.dll` | **Shared** (host's copy) | BCL, always in host |
| `Newtonsoft.Json.dll` | **Isolated** (your copy) | Host may not have it, loaded from your dir |
| Your own helpers | **Isolated** (your copy) | Private to your module |

> **CopilotNote:** The host-wins policy means if both host and plugin have `Newtonsoft.Json`
> but at different versions, the **host's version wins**. Full side-by-side versioning is
> planned for Phase 2.8. For now, target versions compatible with the host~ 💖

### The magic: `CopyLocalLockFileAssemblies`

By default, `dotnet build` does NOT copy NuGet dependency DLLs to your output folder — it
relies on the global NuGet cache and `.deps.json` paths. This works fine when deploying to
a server that runs `dotnet publish`, but **breaks when you copy just the DLL to a plugins
folder**.

**Fix: add this to your `.csproj`:**

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>

  <!-- REQUIRED for plugin scenarios: copies all dependency DLLs to output folder -->
  <!-- This ensures your deps are available next to your DLL when loaded at runtime -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```

With this set, `dotnet build` (and `dotnet publish`) will copy all your private NuGet
dependencies into your output directory, and `.deps.json` will reference them by relative
path. The `AssemblyDependencyResolver` then finds them automatically~ ✨

### Full recommended .csproj for a module

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Plugin support: copy deps next to DLL so AssemblyDependencyResolver finds them -->
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>

    <!-- Prevent host assemblies from being copied (they're shared from host) -->
    <!-- These are already in the host process, no need to ship duplicates -->
    <ExcludeAssets>runtime</ExcludeAssets>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference host contracts — mark as PrivateAssets so they are NOT copied to output -->
    <!-- The host already has these; shipping them would cause type-identity conflicts! -->
    <PackageReference Include="GlutenFree.DotFlow.Core"
                      Version="1.0.0"
                      ExcludeAssets="runtime"
                      PrivateAssets="all" />
    <PackageReference Include="GlutenFree.DotFlow.Modules"
                      Version="1.0.0"
                      ExcludeAssets="runtime"
                      PrivateAssets="all" />

    <!-- Your private dependencies — these WILL be copied to output -->
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Dapper" Version="2.1.28" />
  </ItemGroup>

</Project>
```

> **Key insight:** Use `ExcludeAssets="runtime"` on the host contract packages so their DLLs
> are NOT copied to your output folder. This prevents the plugin from accidentally shadowing
> the host's version and breaking type identity~ 🎯

---

## 4. What to Ship (Plugin Folder Layout)

After `dotnet publish -c Release`, copy the following to your plugins directory:

```
plugins/
  mycompany-http-module/
    MyCompany.DotFlow.MyModule.dll         <- your module assembly
    MyCompany.DotFlow.MyModule.deps.json   <- dependency manifest (REQUIRED!)
    Newtonsoft.Json.dll                    <- private dep (if not in host)
    Dapper.dll                             <- private dep (if not in host)
    -- DO NOT INCLUDE --
    Workflow.Core.dll                      <- already in host, will cause type conflicts
    Workflow.Modules.dll                   <- already in host, will cause type conflicts
    Microsoft.Extensions.*.dll             <- already in host
```

> **DO NOT** ship `Workflow.Core.dll`, `Workflow.Modules.dll`, or any `Microsoft.Extensions.*`
> assemblies with your plugin. The host already has these and the ALC will share them.
> Shipping them causes type-identity conflicts where the cast to `IWorkflowModule` silently
> fails~ 💔

---

## 5. Optional: Use [WorkflowModule] Attribute

You can use the `[WorkflowModule]` attribute to override metadata at discovery time without
changing the class itself — useful for subclassing or packaging scenarios:

```csharp
// Override the module ID registered in the registry (e.g. for rebranding)
[WorkflowModule(
    ModuleId = "acme.http.get",
    Category = "ACME HTTP",
    Description = "ACME-branded HTTP GET module.")]
public sealed class MyHttpModule : IWorkflowModule
{
    // The ModuleId property here is ignored — attribute wins!
    public string ModuleId => "mycompany.http.get";
    // ...
}

// Exclude a module from auto-discovery entirely
[WorkflowModule(Ignore = true)]
public sealed class InternalHelperModule : IWorkflowModule { /* ... */ }
```

---

## 6. Loading Your Module at Runtime

Use `AssemblyModuleLoader` in the DotFlow host to load your plugin:

```csharp
var registry = serviceProvider.GetRequiredService<IModuleRegistry>();
var loader = new AssemblyModuleLoader(registry);

// Load a single assembly
var result = loader.LoadFromAssembly("/plugins/mycompany-http-module/MyCompany.DotFlow.MyModule.dll");

if (result.Success)
{
    Console.WriteLine($"Loaded {result.LoadedModules.Count} module(s)!");
}
else
{
    Console.WriteLine($"Load failed: {string.Join(", ", result.Errors)}");
}

// Load all plugins from a directory
var results = loader.LoadFromDirectory("/plugins/");

// Unload when done (e.g., on plugin hot-swap)
loader.UnloadAssembly("/plugins/mycompany-http-module/MyCompany.DotFlow.MyModule.dll");
```

---

## 7. Validation Rules Cheat Sheet

The `ModuleValidator` runs automatically when your module is registered. Here's what it checks:

| Rule | Requirement |
|---|---|
| `ModuleId` | Lowercase, matches `^[a-z][a-z0-9._-]*$`, max 128 chars |
| `DisplayName` | Not null or empty |
| `Description` | Not null or empty |
| `Category` | Not null or empty |
| `Version` | Not null |
| `Icon` | Not null or empty *(strict mode only)* |
| Port names | Unique within inputs; unique within outputs |
| Port `DataType` | Not null on all ports |
| Port descriptions | Not null or empty *(strict mode only)* |
| Property names | Unique across all properties |

---

## 8. Troubleshooting

### "Module was skipped — failed validation"

Check the host logs for `ModuleValidator` warnings. Common causes:

- Module ID contains uppercase or starts with a digit
- `DisplayName`, `Description`, or `Category` is empty
- Duplicate port names in `Schema.Inputs` or `Schema.Outputs`

### "Assembly loaded but 0 modules registered"

- All your module classes may have `[WorkflowModule(Ignore = true)]`
- Your module classes may be `internal` instead of `public`
- Your module classes may be `abstract`
- The module IDs may already be registered (duplicate detection)

### "FileNotFoundException on a dependency at runtime"

Your `.deps.json` is missing or the dependency DLL is not next to your module DLL. Make sure:

1. `CopyLocalLockFileAssemblies=true` is in your `.csproj`
2. You ran `dotnet publish` (not just `dotnet build`) before copying files
3. The `.deps.json` file is in the same directory as your `.dll`

### "InvalidCastException: cannot cast to IWorkflowModule"

You accidentally shipped `Workflow.Core.dll` or `Workflow.Modules.dll` alongside your plugin.
Remove them — the host already has these and the ALC will use the host's version.
Use `ExcludeAssets="runtime"` on those package references~ 🎯

---

## 9. Checklist Before Shipping

- [ ] Module ID follows `^[a-z][a-z0-9._-]*$` naming convention
- [ ] `Version` property returns a non-null `System.Version`
- [ ] All ports have unique names and non-null `DataType`
- [ ] `.csproj` has `CopyLocalLockFileAssemblies=true`
- [ ] Host contracts (`Workflow.Core`, `Workflow.Modules`) use `ExcludeAssets="runtime"`
- [ ] `.deps.json` is present in the output/publish folder
- [ ] `Workflow.Core.dll` and `Workflow.Modules.dll` are NOT in the plugin folder
- [ ] Module class is `public sealed` (not `internal`, not `abstract`)

---

## 📦 The `.wfmod` Package Format (Phase 2.8)

A `.wfmod` is a ZIP archive that bundles your module DLL(s) with a manifest so DotFlow can install,
version, and manage it over the REST API. Structure:

```
my-module.wfmod  (ZIP)
├── module.json          # the manifest (required, at the root)
├── lib/                 # your entry DLL + private dependencies (incl. its .deps.json)
│   ├── MyCompany.MyModule.dll
│   └── MyCompany.MyModule.deps.json
├── docs/                # optional — README, changelog, examples
└── assets/              # optional — icons, screenshots
```

### `module.json` manifest

```jsonc
{
  "id": "mycompany.mymodule",          // matches IWorkflowModule.ModuleId
  "version": "1.2.0",                   // SemVer-style major.minor.patch
  "displayName": "My Module",
  "description": "Does something useful.",
  "author": "MyCompany",
  "minEngineVersion": "1.0.0",          // optional — see the engine gate below
  "entryAssembly": "lib/MyCompany.MyModule.dll",
  "dependencies": [                     // optional — other modules this one needs
    { "id": "builtin.http.request", "minVersion": "1.0.0", "maxVersion": "2.0.0" }
  ],
  "contentHashes": {                    // optional but recommended — see integrity below
    "lib/MyCompany.MyModule.dll": "<base64 SHA-256 of the file>"
  }
}
```

### 🔢 Engine version gate (`minEngineVersion`)

The DotFlow engine is **SemVer-versioned**. On install, the engine version is compared against your
`minEngineVersion`: **if the running engine is older, the install is refused with `422`**. Set this
to the lowest engine version your module is known to work with (omit it if you don't need a floor).

### 🔐 Package integrity (`contentHashes`)

`contentHashes` maps package-relative file paths → base64 SHA-256 hashes. When present, every listed
file is verified on import and a **mismatch rejects the package** (tamper-evidence). When **absent**,
the package still installs but **trips a warning on import** — always ship hashes for production
modules. Compute a hash in C# with `Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(path)))`.

### 🔗 Dependencies

Declare cross-module dependencies in `dependencies` (and/or via `IWorkflowModule.Dependencies`).
DotFlow resolves them in **topological order**, detects cycles, and refuses to install a package
whose dependencies aren't present. Uninstalling a module that others depend on is refused (`409`).

### 🔢 Side-by-side versions & pinning

Multiple versions of the same module id can be installed at once. By default a workflow node resolves
to the **latest enabled** version; pin a specific version on a node via
`Metadata["moduleVersion"] = "1.2.0"`. Disabled versions are skipped by latest-resolution and fail
validation when pinned.

### 🔄 Hot-reload (opt-in)

When `Modules:HotReload:Enabled=true`, DotFlow watches the installed-packages root (and, when
`Modules:HotReload:WatchLooseDlls=true`, loose dev DLL folders) and reloads changed modules — but
**never while an execution using that module is in flight** (it defers until executions drain).

### 🔏 Signing (optional)

Assemblies may be strong-name signed. By default unsigned/untrusted assemblies **load with a
warning**; set `Modules:Security:RequireSigned=true` (and `Modules:Security:TrustedPublicKeyTokens`)
to **block** them.

### Installing over HTTP

`POST /api/v1/modules/upload` (multipart, field name `package`) installs a `.wfmod`; see
[`rest-api.md`](rest-api.md#modules--apiv1modules) for the full management API (upload / enable /
disable / uninstall).

---

*Made with 💖 by Ami-Chan! Happy module building, senpai~ UwU* ✨

