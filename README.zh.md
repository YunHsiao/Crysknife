<!--
SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
SPDX-License-Identifier: MIT
-->

# Crysknife

当在 Unreal® 引擎中通过扩展的形式来实现具有一定复杂度的引擎层特性时，由于引擎架构的限制，往往很难，或完全无法保证不修改任何引擎源码。

实际上最后的实现往往是完全分散在引擎的各个模块，只能适用于项目内部自己维护的固定版本的引擎仓库，而难以移植到任何其他版本，或其他项目的引擎中。

本项目可以帮助彻底自动化整个“代码注入”的流程，并提供强大的定制能力，使得技术产品可以以扩展的形式真正独立发布和部署，即使功能上涉及直接定制 UE 引擎的已有接口和结构。不管是内部引擎版本升级，还是将复杂技术产品迁移到任何新的项目代码库，都可以无痛一键部署。

背后的工作原理如下：

所有对引擎源码的修改，会保存为增量 Patch：
* 应用 Patch 时会做模糊匹配，可自定义阈值
* 对不同版本的引擎修改会自动保存为不同的 Patch 文件，来避免 Patch 上下文差异过大导致无法匹配
* 应用 Patch 时会自动选择对目标代码库最匹配的版本
* 所有 Patch 都严格可逆，多次 Patch 无任何重复
* 如果应用失败，输出错误 Log 中会包含一份完整的 Diff HTML 来帮助手动处理冲突

> 只有会产生修改的行为才会被实际执行，所以程序执行完毕后 Console 没有相关输出意味着所有文件已是最新状态。

所有要在引擎模块中新增的文件，都会自动被拷贝或链接到目标目录。

> 虽然听起来理想，但创建链接到引擎源码目录会导致实际源文件更新时，编译器无从知晓这个文件要被重新编译，会引起不必要的麻烦，所以拷贝为默认行为。

更复杂的注入行为可以使用 [Config](#Config-系统) 系统配置。

## 环境搭建

将仓库 Clone 到引擎扩展目录：
```bash
Engine/Plugins/Crysknife/
```

选择和系统匹配的脚本执行生成部署脚本命令：
```bash
Crysknife.sh -P ${PluginName} -S
```

这会在指定的扩展目录下生成几个 `Setup` 脚本，作为所有操作的入口。

程序会尝试从指定扩展的以下目录搜索 Patch 并应用到 `Engine/Source`:
```bash
Engine/Plugins/${PluginName}/SourcePatch/
```
从 `SourcePatch` 文件夹起的所有相对目录结构默认都会完整保留。

Injector 本身相对简单直接，并不会神奇地自动改变任何代码结构，更多的是开发者自己对架构设计的权衡，以下是一些推荐的通用原则：
* 实现功能时尽量优先依赖引擎内置的扩展和回调系统，即使不依赖任何注入也可以做到很多事
* 尽量把需要注入的代码都拆分到独立的新文件，在引擎源码中的注入点越少越好
* 根据经验，超过 90% 的代码可以直接正常写在扩展内，其余注入代码里超过一半可以组织到新的引擎文件中

## Patch 语法

在引擎源码中新增代码，需要遵守固定的语法规则才可以被 Injector 识别，所有修改都要加注释桩：

### 多行形式

```cpp
// ${Tag}${Comments}: Begin
** YOUR CODE BLOCK HERE **
// ${Tag}: End
```

### 单行形式

```cpp
** YOUR ONE-LINER HERE ** // ${Tag}${Comments}
```

### 下行形式
注意此形式下注释桩独占一行，行内不可以有任何有效代码：

```cpp
// ${Tag}${Comments}
** YOUR ONE-LINER HERE **
```

注释桩的 Tag 默认为扩展文件夹名，如果需要可以通过 [Config 系统](#内置变量) 修改。

> 如果不影响性能的话，尽量选择最有代表性的注入点，如类的开始，长期存在的公开接口后等，可以大大提高在不同版本的引擎间自动匹配 Patch 的成功率。

对于要直接修改的引擎源码，遵循如下流程：
* 将要修改的引擎代码全部注释掉（暂只支持行注释 "//" ）
* 将注释掉的代码加任何一种上述形式的注释桩，外加一个小变化*
* 在此之上再按标准流程，继续正常新增你自己的代码

这个小变化是：

```cpp
// ${Tag}-${Comments}
```

注释中扩展名字后面的 **减号** 至关重要，它会告诉 Injector 接下来这个代码块是直接来自原版引擎源码的，在应用 Patch 时应该直接通过注释的形式删除。

> 在多行形式的注释桩中，结尾注释可以省略减号。

如果的确要删改大段引擎源码，也可以用宏包围，这样不需要逐行修改：

```cpp
#if 0 // ${Tag}${Comments}
** LARGE SOURCE BLOCK **
#endif // ${Tag}${Comments}
```

## 命令行参数

* `-P [PLUGIN]` 输入扩展的文件夹名，默认也是注释桩中要匹配的关键词，必须指定
* `-E [ENGINE_ROOT]` 指定引擎根目录，默认为本仓库所在引擎
* `-D [VAR=VALUE,]...` 定义 Config 中的变量值

### 行为类

* `-S` 为指定扩展生成必要的配置和脚本，只需运行一次
* `-R [FILES|DIRECTORIES]...` 从指定的文件或目录搜索，注册任何发现的有效 Patch 并执行 “生成” 行为
* `-U [FILES|DIRECTORIES]...` 从指定的文件或目录搜索，删除任何发现的有效 Patch 并执行 “生成” 行为
* `-G` 生成 / 更新 Patch
* `-C` 从引擎源码目录清除任何已应用的 Patch
* `-A` 拷贝所有新文件，应用所有 Patch 到引擎源码目录（默认行为）

> 所有行为可以相互组合：  
> 如指定 `-GA` 执行生成 + 应用, 指定 `-GC` 执行生成 + 清除等。 

### 定制类

* `-i [FILTER]...` 或 `--inclusive-filter [FILTER]...` 所有行为只对指定路径生效
* `-e [FILTER]...` 或 `--exclusive-filter [FILTER]...` 所有行为只对指定路径不生效
* `-l` 或 `--link` 链接而非拷贝新文件
* `-f` 或 `--force` 强制覆盖任何已存在的文件
* `-d` 或 `--dry-run` 测试执行，所有输出会被安全映射到扩展目录的 `Intermediates/Crysknife/Playground` 下
* `-v` 或 `--verbose` 详细 Log 模式
* `-t` 或 `--treat-patch-as-file` 将 Patch 视为普通文件，直接执行拷贝/链接
* `-c` or `--clear-all-history` 清除所有现存 Patch，从零生成
* `-k` or `--keep-all-history` 保留所有现存 Patch，只更新与当前引擎版本相关的内容

### 参数类

* `--patch-context [LENGTH]` 生成 Patch 时的上下文长度，默认 250
* `--content-tolerance [TOLERANCE]` 应用 Patch 时的内容匹配阈值，范围 [0, 1]， 默认 0.3
* `--line-tolerance [TOLERANCE]` 应用 Patch 时的行号匹配阈值，默认无限大（不同版本引擎的行号可能差异巨大）

## 命令行用法示例

<details>
<summary>新增文件到引擎源码</summary>

比如我们要新增一个 `MyEnginePlugin.cpp` 到 `Engine/Source/Runtime/Engine/Private`：

* 在 `${PluginRoot}/SourcePatch/Runtime/Engine/Private` 目录下创建 `MyEnginePlugin.cpp`
* `Setup.sh` (默认执行“应用”行为)
* 新文件应该已在引擎源码的相同目录结构下创建

</details>

<details>
<summary>修改现有引擎源码</summary>

比如我们要修改某个已有的引擎源文件：
* 直接进入这个源文件做修改，记得加入注释桩
* `Setup.sh -R ${FullPathToModifiedEngineSourceFile}`
* `Setup.sh -G` 提交前再执行一遍，更新所有 Patch 内容

> 版本发布前建议检查清除所有 Patch 后的代码是否依然正常：（可能需要全量编译）  
> `Setup.sh -GC` 生成 + 清除  
> 可以用来确认所有相关的修改都加了注释桩

</details>

<details>
<summary>从引擎源码移除 Patch</summary>

比如我们希望彻底移除某个文件的 Patch：

* `Setup.sh -U ${PathToEngineSourceToBeUnpatched}...`
* 源文件应该已清除任何 Patch，相关的 Patch 文件也应已删除

如果我们希望只临时移除 `Engine/Source/Runtime/Engine` 文件夹内的 Patch:

* `Setup.sh -Ci Runtime/Engine` 清除 Patch
* `Setup.sh -i Runtime/Engine` 重新应用 Patch

</details>

<details>
<summary>移植修改到完全不同的引擎版本</summary>

* `Setup.sh`
* 可通过调整 `--content-tolerance` 参数或手动对照 Diff HTML 处理出现的冲突
* `Setup.sh -G`
* 一套匹配当前引擎版本的新的 Patch 应已在 SourcePatch 目录生成

</details>

## Config 系统

`SourcePatch` 根目录下可以新建一个 `Crysknife.ini` 配置文件，来指定如条件重映射等更复杂的 Patch 行为。配置框架如下：

```ini
; Declare any variables you need
[Variables]
Var1=Value4
Var2=True

; Declare source patch in other plugins that you will depend on
[Dependencies]
; Variables can be overridden with your own ones
Plugin1=Var3=${Var2}
; It's okay to have no overrides
Plugin2=

; Applies to all files
[Global]
; Multiple conditions are allowed
Rule1=Predicate1:Value1|Value2
; Or add them in separate lines
+Rule1=Predicate1:Value3
; Variable references & reverse dependencies
+Rule1=Predicate3:${Var1},Predicate4:!Value5

[Path/To/Dir1]
; Only apply to the specified subdirectory
ScopedRule1=Predicate2

; This scope automatically extends from the above parent scope
[Path/To/Dir1/Folder1]
ScopedRule2=Predicate5

; Multiple directories are allowed
[Path1|Path2]
```

* Global 作用域 (Section) 下的规则 (Rule) 会应用到所有子目录，其他作用域的规则只会应用到指定子目录下
* 可同时声明以 `|` 分割的多个子目录，作用域内规则会对所有子目录生效
* 如果存在多个作用域都对同一个文件生效，根据指定的路径层级，内层配置自动继承外层配置
* 如无特殊声明，每个作用域可以有多条规则，每条规则可以有多个条件 (Predicate)，每个条件可以有多个值 (Value)
* Variables 作用域内可定义任意变量 (Variable)， 在任意值内都可通过 `${VariableName}` 引用
* 任意值都可以添加 `!` 前缀，表示反向条件（条件不成立时满足）
* Dependencies 作用域内可用于指定对其他引擎扩展内的 Patch 的依赖，以提供对多扩展架构的无缝支持

> 在主配置文件之外，还可以创建一份独立的“本地”配置（`CrysknifeLocal.ini`）来覆盖主配置。此配置默认不会提交到扩展的 git 仓库，但应当提交到引擎主仓库（如 P4）。
> 对于引擎代码库相关的参数，如注释桩和引擎名等，使用本地配置更合适。

### 规则

`SkipIf=[PREDICATE]...`
* 如果条件满足，跳过执行行为

`RemapIf=[PREDICATE]...`
* 如果条件满足，重映射行为目标到新路径

`RemapTarget=[PATH]`
* 重映射的新路径目标，会直接全文替换目标目录中当前 Section 名字的部分
* 如果指定了 `RemapIf`，此条必须指定

`FlattenIf=[PREDICATE]...`
* 如果条件满足，不保留目录结构，展平所有输出到同一层级

`^Base[RULE]=...`
* 任意规则名称加 `Base` 前缀，表明当前行的条件在基作用域生效，也只能被基作用域的指令覆盖
* 当且仅当希望覆盖任何 [BaseCrysknife.ini](BaseCrysknife.ini) 中定义的规则时再使用

### 条件

`TargetExists:[FILE|DIRECTORY]...`
* 任何指定的文件/目录存在时满足

`IsTruthy:[SWITCH]...`
* 任何指定的值为真时满足

`NameMatches:[NAME]...`
* 当前输入文件名匹配时满足

`NewerThan:[VERSION]...`
* 当前引擎版本大于等于指定值（如 `5.0`）时满足

`Conjunctions:All|Predicates|Root|TargetExists|IsTruthy|NameMatches...`
* 将指定范围的组合逻辑设为“与”
* `Root` 指所有不同条件间的组合逻辑
* `Predicates` 指所有定义的条件内的组合逻辑
* `All` 等价于 `Predicates|Root`
* 默认所有条件内和条件之间都是逻辑或关系

`Always` / `Never`
* 总是满足 / 总是不满足

### 内置变量

* `CRYSKNIFE_PLUGIN_DIRECTORY`: 默认值为目标扩展根目录的完整路径，只读
* `CRYSKNIFE_SOURCE_DIRECTORY`: 默认值为引擎源码根目录的完整路径，只读
* `CRYSKNIFE_COMMENT_TAG`: 默认值为当前扩展的文件夹名，可自定义为其他更具区分度的标识符
* `CRYSKNIFE_ENGINE_TAG`: 默认值为当前引擎版本号（`[MAJOR].[MINOR]`），可在本地配置中自定义为其他更具区分度的标识符

## Config 用法示例

<details>
<summary>条件跳过</summary>

如果目标目录不存在就跳过执行：

```ini
[Programs/UnrealHeaderTool]
SkipIf=TargetExists:!Programs/UnrealHeaderTool
```

</details>

<details>
<summary>展平重映射</summary>

将所有 Patch 展平目录结构重映射输出：

```ini
[Global]
RemapIf=Always
RemapTarget=Test
Flat=True
```

> 类似机制可通过 Dry Run 模式 (`-d`) 直接使用。

</details>

## 内置 Patch

我们在 `SourcePatch` 文件夹内置了一些可能有用的工具，可以提供一些有趣的权衡。

|                                      头文件路径                                       |  模块  |            备注            |
|:--------------------------------------------------------------------------------:|:----:|:------------------------:|
| [Misc/PrivateAccessor.h](SourcePatch/Runtime/Core/Public/Misc/PrivateAccessor.h) | Core |  允许在非友元环境下访问私有变量或函数的小工具  |
