# Unreal Source Injector

Inject source files & code segments into any existing Unreal Engine code base.  
All injections are strictly reversable with a single command.  
More complex behaviors can be specified with the [config system](#Config-System).  

This repository is meant to be used as part of an engine plugin to help
quickly deploying features to different engine bases.

You should clone this project to your engine plugin:
```
Engine/Plugins/${ProjectName}/UnrealSourceInjector/
```
And code will be injected from the following directory to `Engine/Source`:
```
Engine/Plugins/${ProjectName}/SourcePatch/
```
All relative structures from the `SourcePatch` directory will be preserved,
new source files can be either copied or symbolically linked into target directory.

> Although tempting, symbolic links doesn't update with source file changes which may cause
> new updates to be ignored by compiler during development, so copy is made the default operation.

For code segments we detect & inject in the following forms (with comment guards):

1. Multi-line:

```cpp
// ${ProjectName}${Comments}: Begin
** YOUR CODE BLOCK HERE **
// ${ProjectName}: End
```

2. Single-line:

```cpp
** YOUR ONE-LINER HERE ** // ${ProjectName}${Comments}
```

3. Next-line: Note that there can be no code at the same line with the comment guard:

```cpp
// ${ProjectName}${Comments}
** YOUR ONE-LINER HERE **
```

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

Patches generated for these injections are fuzzy matched with customizable tolerances.
Error messages will be received when patching fails, with a full file diff HTML to help you manually resolve the conflicts.

## Usage

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
* `./Injector.sh --add ${FullPathToModifiedEngineSourceFile}`
* `./Injector.sh -G` afterwards to update all patches before committing

### Remove Existing Patch From Engine Source

Say we want to permanently remove all our previous modification from some existing engine source file:

* `./Injector.sh --rm ${FullPathToModifiedEngineSourceFile}`
* The source file should be un-patched and the relevant patch files are deleted

If we only want to temporarily remove the patches from all files under `Engine/Source/Runtime/Engine`:

* `./Injector.sh -C -I Runtime/Engine` (To un-patch source files)
* `./Injector.sh -I Runtime/Engine` (To re-apply patches)

## Command Line Options

### Actions

* `-A` Apply patch and link all new sources (default action)
* `-G` Generate/update patch
* `-C` Clear patches from target files
* `-T` Test run
* `--add [FILES|DIRECTORIES]...` Add specified source files to patch list and update
* `--rm [FILES|DIRECTORIES]...` Remove specified source files from patch list and update

### Modifiers

* `-P [PROJECT]` or `--project [PROJECT]` Project name to match in comments
* `-I [FILTER]` or `--inclusive-filter [FILTER]` Inclusive target path filter
* `-E [FILTER]` or `--exclusive-filter [FILTER]` Exclusive target path filter
* `--src [DIRECTORY]` Source directory containing all the patches
* `--dst [DIRECTORY]` Destination directory containing target sources to be patched
* `--link` Make symbolic links instead of copy all the new files
* `--nb` or `--no-builtin` Skip builtin source patches
* `-F` or `--force` Force override existing files

### Parameters

* `--pc [LENGTH]` or `--patch-context [LENGTH]` Patch context length when generating patches, default to 50
* `--ct [TOLERANCE]` or `--content-tolerance [TOLERANCE]` Content tolerance in [0, 1] when matching sources, default to 0.5
* `--lt [TOLERANCE]` or `--line-tolerance [TOLERANCE]` Line tolerance when matching sources, default to infinity (line numbers may vary significantly between engine versions)

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
