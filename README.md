# Unreal Source Injector

Inject source files & code segments into existing Unreal Engine code base.

This repository is meant to be used as part of an engine plugin. 

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

> Creating symbolic links requires administration privileges on Windows devices.

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

```shell
dotnet build # One-time setup 
dotnet run -- [Args...]
```

### New Engine Source Files

Say we are adding a new source file under `Engine/Source/Runtime/Engine/Private` named `MyEnginePlugin.cpp`:

* Create `MyEnginePlugin.cpp` under `${ProjectRoot}/SourcePatch/Runtime/Engine/Private`
* `dotnet run` (by default runs the apply action)
* The source file should be present under the same hierarchy inside engine source directory

### Modify Existing Engine Source

Say we want to modifying some existing engine source file:

* Go ahead and modify the engine source directly, remember to add the aforementioned comment guards
* `dotnet run -- --add ${FullPathToModifiedEngineSourceFile}`
* `dotnet run -- -G` afterwards to update all patches before committing

### Remove Existing Patch From Engine Source

Say we want to permanently remove all our previous modification from some existing engine source file:

* `dotnet run -- --rm ${FullPathToModifiedEngineSourceFile}`
* The source file should be un-patched and the relevant patch files are deleted

If we only want to temporarily remove the patches from all files under `Engine/Source/Runtime/Engine`:

* `dotnet run -- -C -F Runtime/Engine` (To un-patch source files)
* `dotnet run -- -F Runtime/Engine` (To re-apply patches)

## Command Line Options

### Actions

* `-A` Apply patch and link all new sources (default action)
* `-G` Generate/update patch
* `-C` Clear patches from target files
* `-T` Test run
* `--add [FILES|DIRECTORIES]...` Add specified source files to patch list and update
* `--rm [FILES|DIRECTORIES]...` Remove specified source files from patch list and update

### Modifiers

* `-P PROJECT` or `--project PROJECT` Project name to match in comments
* `-I FILTER` or `--inclusive-filter FILTER` Inclusive filter target path
* `-E FILTER` or `--exclusive-filter FILTER` Exclusive filter target path
* `--src DIRECTORY` Source directory containing all the patches
* `--dst DIRECTORY` Destination directory containing target sources to be patched
* `--link` Make symbolic links instead of copy all the new files
* `--nb` or `--no-builtin` Skip builtin source patches
* `-F` or `--force` Force override existing files

### Parameters

* `--pc LENGTH` or `--patch-context LENGTH` Patch context length when generating patches, default to 50
* `--ct TOLERANCE` or `--content-tolerance TOLERANCE` Content tolerance in [0, 1] when matching sources, default to 0.5
* `--lt TOLERANCE` or `--line-tolerance TOLERANCE` Line tolerance when matching sources, default to infinity (line numbers may vary significantly between engine versions)

## Builtin Source Patches

|                                   Include Path                                   | Module |                                Comment                                |
|:--------------------------------------------------------------------------------:|:------:|:---------------------------------------------------------------------:|
| [Misc/PrivateAccessor.h](SourcePatch/Runtime/Core/Public/Misc/PrivateAccessor.h) |  Core  | A tiny library for accessing private members from non-friend contexts |
