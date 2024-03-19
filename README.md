# Unreal Source Injector

When implementing plugins with complex engine-level customizations for Unreal Engine™,
due to many design decisions of the engine architecture,
it is very hard, if not impossible to keep away from modifying stock engine files to make everything fully works.

In fact oftentimes the changes are completely scattered across engine modules,
which could be fine for one in-house engine base, but extremely hard to port to any other.

This project aims to completely automate the injection process, with powerful customization capabilities.

Changes in existing engine files are stored as patches:
* Patches are fuzzy matched with customizable tolerances
* Multiple patches can be generated targeting at different engine versions when they become just too diverged to be fuzzy matched
* When applying patches, the closest matched version to destination engine base will be used
* All injections are strictly reversible with a single command
* As the last resort when patching fails, the error message comes with a full file diff HTML to help you manually resolve the conflicts

> Only actions making observable differences are executed, so an empty console output means everything's up-to-date.

New source files can be either copied or symbolically linked into target directory.

> Although tempting, symbolic links doesn't update with source file changes which may cause
> new updates to be ignored by compiler during development, so copy is made the default operation.

More complex injection behaviors can be specified with the [config system](#Config-System).

# Environment Setup

You should clone this project under an engine plugin:
```
Engine/Plugins/${ProjectName}/UnrealSourceInjector/
```
And code patches will be read from the following directory to `Engine/Source`:
```
Engine/Plugins/${ProjectName}/SourcePatch/
```
All relative structures from the `SourcePatch` directory will be preserved.

The injector itself doesn't magically change the way your code is organized. Here are the recommended general principles:
* Only inject sources when there is no way around, you can still go very far with only the builtin plugin system
* Try to move as much code as possible to separate new files instead of embedding them inline into existing engine base
* Empirically 90% of the code in new source files is considered a good ratio

# Patch Syntax

For changes in existing engine files we detect & inject in the following forms (with comment guards):

### Multi-line

```cpp
// ${ProjectName}${Comments}: Begin
** YOUR CODE BLOCK HERE **
// ${ProjectName}: End
```

### Single-line

```cpp
** YOUR ONE-LINER HERE ** // ${ProjectName}${Comments}
```

### Next-line
Note that there can be no code at the same line with the comment guard:

```cpp
// ${ProjectName}${Comments}
** YOUR ONE-LINER HERE **
```

> Try to find the most representative context to insert your code (beginning of class, after long-standing public interfaces, etc.),
> which can greatly increase the chances of finding matches in different engine bases.

Additionally, for modifying stock engine code, follow these steps:
* Comment out the original code block (only line comments are supported atm.)
* Guard the comment block using any of the above forms, with one special tweak*
* Add a new guarded block normally for your own code

Where the special tweak is:

```cpp
// ${ProjectName}-${Comments}
```

**The minus sign** after the project name is enough to tell the injector that the surrounding code segment is directly from stock engine source, essentially making it a deletion block.

> The minus sign can be omitted in the ending comment for multi-line guards.

## Command Line Interface

### Actions

* `-R [FILES|DIRECTORIES]...` Search specified file or directory, register any patched file and generate
* `-U [FILES|DIRECTORIES]...` Search specified file or directory, unregister any patched file and update
* `-G` Generate/update patches
* `-C` Clear patches from target files
* `-A` Apply existing patches and copy all new sources (default action)

> Actions are combinatorial:  
> i.e. `-G -A` for generate & apply (round trip), `-G -C` for generate & clear (retraction), etc. 

### Modifiers

* `-p [PROJECT]` or `--project [PROJECT]` Project name to match in comments
* `-s [DIRECTORY]` or `--src [DIRECTORY]` Customize the source directory where the patches are located
* `-d [DIRECTORY]` or `--dst [DIRECTORY]` Customize the destination directory containing target sources to be patched
* `-l` or `--link` Make symbolic links instead of copy all the new files
* `-t` or `--dry-run` Test run, safely executes the action with all engine output remapped to `SourcePatch` directory
* `-f` or `--force` Force override existing files
* `-b` or `--no-builtin` Skip builtin source patches

### Parameters

* `--if [FILTER]` or `--inclusive-filter [FILTER]` Inclusive target path filter
* `--ef [FILTER]` or `--exclusive-filter [FILTER]` Exclusive target path filter
* `--pc [LENGTH]` or `--patch-context [LENGTH]` Patch context length when generating patches, default to 50
* `--ct [TOLERANCE]` or `--content-tolerance [TOLERANCE]` Content tolerance in [0, 1] when matching sources, default to 0.5
* `--lt [TOLERANCE]` or `--line-tolerance [TOLERANCE]` Line tolerance when matching sources, default to infinity (line numbers may vary significantly between engine versions)

## CLI Examples

Use the script file matching your operating system:
* `Injector.sh` for Linux
* `Injector.command` for Mac
* `Injector.bat` for Windows

### New Engine Source Files

Say we are adding a new source file under `Engine/Source/Runtime/Engine/Private` named `MyEnginePlugin.cpp`:

* Create `MyEnginePlugin.cpp` under `${ProjectRoot}/SourcePatch/Runtime/Engine/Private`
* `./Injector.sh` (by default runs the apply action)
* The source file should be present under the same hierarchy inside engine source directory

### Modify Existing Engine Source

Say we want to modifying some existing engine source file:
* Go ahead and modify the engine source directly, remember to add the aforementioned comment guards
* `./Injector.sh -R ${FullPathToModifiedEngineSourceFile}`
* `./Injector.sh -G` afterwards to update all patches before committing

> During development it is recommended to periodically check the cleared source still works:  
> `./Injector.sh -G -C` (The retraction action)  
> This can be used to ensure all relevant changes are properly guarded.

### Remove Existing Patch From Engine Source

Say we want to permanently remove all our previous modification from some existing engine source file:

* `./Injector.sh -U ${PathToEngineSourceToBeUnpatched}...`
* The source file should be un-patched and the relevant patch files will be deleted

If we only want to temporarily remove the patches from all files under `Engine/Source/Runtime/Engine`:

* `./Injector.sh -C --if Runtime/Engine` (To un-patch source files)
* `./Injector.sh --if Runtime/Engine` (To re-apply patches)

### Porting To A Completely Different Engine Base

* `./Injector.sh`
* Resolve potential conflicts by either adjust the `--content-tolerance` parameter or inspecting
the reference diff HTML & manually patch in (remember the comment guards)
* `./Injector.sh -G`
* A new set of patches matching the current engine version will be generated and ready to be committed

## Config System

Every `SourcePatch` directory can have one config file `Injector.ini` at the root,
to specify more complex patching behaviors such as conditional remapping, etc. in the following framework:

```ini
[Global]
Rule1=Predicate1:Condition1|Condition2
+Rule1=Predicate3:Condition3,Predicate4:Condition4

[Path/To/Subdirectory/Or/Filename]
ScopedRule=Predicate2
```

The global section applies the rule to all subdirectories inside `SourcePatch` folder, while custom sections with
relative file/directory paths can be used to limit the effective scope of the rules.

### Supported Rules

`SkipIf=[PREDICATE]...`
* Skip action if specified predicates are satisfied

`RemapIf=[PREDICATE]...`
* Remap action to a different destination directory if specified predicates are satisfied

`RemapTarget=[PATH]`
* The remap destination, which would be replacing the section name part of the input file path
* Must be specified if `RemapIf` is present

`Flat=True|False`
* Whether to flatten the folder hierarchy under current scope

### Supported Predicates

`Exist:[FILE|DIRECTORY]...`
* Satisfies if any of the specified file/directory exists

`Filename:[NAME]...`
* Satisfies if the input file name matches

`Conjunction:All|Predicates|Root|Exist|Filename...`
* Changes the logical behavior of specified predicate to conjunction (logical AND)
* `Root` means the logical operations between different predicates
* `Predicates` means all logical operations inside every defined predicate
* `All` means `Predicates|Root`
* (By default all conditions are disjunction, i.e. the results are logically OR-ed together)

`^BaseDomain`
* Indicating current rule line is in base domain, therefore can only be overrule by the same domain directives
* Use this iff you want to override base rules defined in [BaseInjector.ini](BaseInjector.ini)
* Must be defined at the start of the current rule.

`Always`
* Always satisfies

## Builtin Source Patches

We included some useful utilities in the builtin `SourcePatch` folder, which can provide some interesting trade-offs.

|                                   Include Path                                   | Module |                                Comment                                |
|:--------------------------------------------------------------------------------:|:------:|:---------------------------------------------------------------------:|
| [Misc/PrivateAccessor.h](SourcePatch/Runtime/Core/Public/Misc/PrivateAccessor.h) |  Core  | A tiny library for accessing private members from non-friend contexts |
