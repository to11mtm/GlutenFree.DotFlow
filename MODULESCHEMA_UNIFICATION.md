# ModuleSchema Unification Summary 📋✨

**Date:** December 27, 2025  
**Status:** ✅ **COMPLETE!**

## Problem

There were **two different ModuleSchema types** in the codebase:

1. **Workflow.Core.Models.ModuleSchema** - Used `PropertyDefinition` with `PropertyType` enum
2. **Workflow.Modules.Abstractions.ModuleSchema** - Used `PortDefinition` with `System.Type` and had UI features

This caused ambiguous type references and confusion about which types to use.

## Solution

Created a **unified schema system** in `Workflow.Core.Models` that:

1. Uses **LanguageExt Arr** for immutable, structurally-equatable collections
2. Has **separate types** for ports (data flow) vs properties (configuration)
3. Uses **System.Type** for runtime type information
4. Includes **UI metadata** for property editors

## New Unified Types (Workflow.Core.Models.ModuleSchema.cs)

### ModuleSchema
```csharp
public record ModuleSchema(
    Arr<PortDefinition> Inputs,
    Arr<PortDefinition> Outputs,
    Arr<ModulePropertyDefinition> Properties)
{
    public static ModuleSchema Empty => new(
        Arr<PortDefinition>.Empty,
        Arr<PortDefinition>.Empty,
        Arr<ModulePropertyDefinition>.Empty);
}
```

### PortDefinition
For input/output connection points between nodes:
```csharp
public record PortDefinition(
    string Name,
    string DisplayName,
    Type DataType,
    string? Description = null,
    bool IsRequired = true,
    object? DefaultValue = null)
{
    // Factory methods
    public static PortDefinition Create(string name, Type dataType, bool isRequired = true);
    public static PortDefinition Create<T>(string name, bool isRequired = true);
}
```

### ModulePropertyDefinition
For configuration settings on the node:
```csharp
public record ModulePropertyDefinition(
    string Name,
    string DisplayName,
    Type DataType,
    string? Description = null,
    bool IsRequired = false,
    object? DefaultValue = null,
    PropertyEditorType EditorType = PropertyEditorType.Text,
    Arr<object>? AllowedValues = null,
    Arr<ValidationRule>? ValidationRules = null,
    HashMap<string, string>? DisplayMetadata = null)
{
    // Factory methods
    public static ModulePropertyDefinition Create(string name, Type dataType, bool isRequired = false);
    public static ModulePropertyDefinition Create<T>(string name, bool isRequired = false);
}
```

### PropertyEditorType (enum)
UI editor types for properties:
- Text, MultilineText, Number, Boolean, Dropdown
- FilePath, DirectoryPath, ConnectionString
- Expression, Json, Code

### ValidationRule & ValidationRuleType
For property validation:
- MinLength, MaxLength, Min, Max, Regex, Enum, Custom

## Key Design Decisions

### 1. Ports vs Properties
- **Ports** are connection points for data flow between nodes (like electrical plugs)
- **Properties** are configuration settings for the node itself (like control knobs)
- Ports default to `IsRequired = true`, Properties default to `IsRequired = false`

### 2. System.Type vs Enum
Using `System.Type` instead of an enum because:
- More expressive (can represent any .NET type)
- Better for generics (e.g., `List<string>`)
- Easier validation with reflection
- More extensible

### 3. LanguageExt Collections
Using `Arr<T>` instead of `IReadOnlyList<T>` because:
- **Structural equality** - Two schemas with same content are equal
- **Immutability** - Thread-safe, no accidental mutations
- **Consistency** - Matches the rest of the codebase

### 4. Factory Methods
Added `Create<T>()` methods for cleaner syntax:
```csharp
// Instead of
new PortDefinition("input", "Input", typeof(string))

// You can write
PortDefinition.Create<string>("input")
```

## Files Modified/Deleted

### Created (consolidated)
- ✅ `Workflow.Core/Models/ModuleSchema.cs` - All unified types (~215 lines)

### Deleted (duplicates removed)
- ❌ `Workflow.Core/Models/PropertyDefinition.cs` - Merged into ModuleSchema.cs

### Updated (to use unified types)
- ✅ `Workflow.Modules/Abstractions/IWorkflowModule.cs` - Uses Core types
- ✅ `Workflow.Modules/Builtin/PassThroughModule.cs` - Uses new schema syntax
- ✅ `Workflow.Tests/Engine/NodeExecutorTests.cs` - Uses Core types
- ✅ `Workflow.Tests/Core/Models/PropertySystemTests.cs` - Tests new types

## Usage Example

```csharp
public class HttpRequestModule : IWorkflowModule
{
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("url", "URL", typeof(string), IsRequired: true),
            PortDefinition.Create<string>("body", isRequired: false)),
        Outputs: Arr.create(
            PortDefinition.Create<string>("response"),
            PortDefinition.Create<int>("statusCode")),
        Properties: Arr.create(
            new ModulePropertyDefinition("method", "HTTP Method", typeof(string),
                EditorType: PropertyEditorType.Dropdown,
                AllowedValues: Arr.create<object>("GET", "POST", "PUT", "DELETE")),
            ModulePropertyDefinition.Create<int>("timeoutSeconds")));
}
```

## Build Status

**✅ 0 Errors, 881 Warnings (all StyleCop/naming conventions)**

The solution builds successfully with all types properly unified.

## Backward Compatibility

- `PropertyType` enum still exists (used by `VariableDefinition`)
- Old code using `Workflow.Core.Models.ModuleSchema` may need minor updates
- `Workflow.Modules.Abstractions` now re-exports from Core

---

> 💝 **Ami's Notes:** The unification was super satisfying! Now we have ONE source of truth for module schemas, and it lives where it should - in Workflow.Core. The LanguageExt Arr collections give us proper structural equality, which is essential for comparing schemas. The factory methods make creating ports and properties much cleaner too! UwU ✨

**Status:** ✅ **UNIFICATION COMPLETE!** 🎉

