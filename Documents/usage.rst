..
   SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
   SPDX-License-Identifier: MIT

Usage
=====

Installation
------------

You should clone this repo as an engine plugin::

   Engine/Plugins/Crysknife/

For first-time setup, you should use the script file matching your operating system and run::

   Crysknife.sh -P ${PluginName} -S

This will generate a few ``Setup`` scripts to your plugin directory as the main entry for any operations.

And code patches will be read from the following directory to ``Engine/Source``::

   Engine/Plugins/${PluginName}/SourcePatch/

All relative structures from the ``SourcePatch`` directory will be preserved.

The injector itself doesn't magically change the way your code is organized.
Here are the recommended general principles:

- Only inject sources when there is no way around it, you can still go very far with only the builtin plugin system
-
   Try to move as much code as possible to separate new files instead of embedding them
   inline into the existing engine base
-
   Empirically more than 90% of the code can be written inside the plugin itself,
   and more than half of the rest injections can be organized into new engine source files

Patch Syntax
------------

For changes in existing engine files we detect & inject in the following forms (with comment guards):

Multi-line
^^^^^^^^^^

.. code-block:: cpp

   // ${Tag}${Comments}: Begin
   ** YOUR CODE BLOCK HERE **
   // ${Tag}: End

Single-line
^^^^^^^^^^^

.. code-block:: cpp

   ** YOUR ONE-LINER HERE ** // ${Tag}${Comments}

Next-line
^^^^^^^^^

Note that there can be no code at the same line with the comment guard:

.. code-block:: cpp

   // ${Tag}${Comments}
   ** YOUR ONE-LINER HERE **

The comment tag is defaulted to plugin folder name but can be modified if needed,
through configs.

.. note::
   For non-performance-critical code, try to find the most representative context to insert your code (beginning of class,
   after long-standing public interfaces, etc.), which can greatly increase the chances of finding matches in different engine bases.

Modifying Existing Code
^^^^^^^^^^^^^^^^^^^^^^^

Additionally, to modify the stock engine code, follow these steps:

- Comment out the original code block (only line comments are supported atm.)
- Guard the comment block using any of the above forms, with one special tweak*
- Add a new guarded block normally for your code

Where the special tweak is:

.. code-block:: cpp

   // ${Tag}-${Comments}

**The minus sign** after the comment tag is enough to tell the injector that
the surrounding code segment is directly from the stock engine source, essentially making it a deletion block.

.. note::
   The minus sign can be omitted in the ending comment for multi-line guards.

If having to modify large blocks of engine code, remember there's always the macro guard approach
which does not need to touch every line of the code:

.. code-block::

   #if 0 // ${Tag}${Comments}
   ** LARGE SOURCE BLOCK **
   #endif // ${Tag}${Comments}

Decorators
----------

To improve the robustness of the fuzzy-match process, etc. on a per-code-block basis,
inline decorators can be specified inside the guarded block in the following format:

.. code-block:: cpp

   // ... Somewhere inside any guarded block
   // @Crysknife(${Directive} = ${Value})

Supported Directives
^^^^^^^^^^^^^^^^^^^^

``MatchContext=<UPPER|LOWER>``
   Limit the fuzzy-matching context to the specified direction,
   by default all contexts are matched

``MatchLength=<LENGTH>``
   For each matching context, only match up to the specified length of the context,
   default is 64 (maximum)

``EngineNewerThan=<VERSION>``, ``EngineOlderThan=<VERSION>``
   Mark the Enclosing code block as engine-version-relevant,
   so it would only apply to matching engine versions

Builtin Source Patches
----------------------

We included some useful utilities in the built-in ``SourcePatch`` folder,
which can provide some interesting trade-offs.

====================== ======== =======
Include Path           Module   Comment
====================== ======== =======
Misc/PrivateAccessor.h Core     A tiny library for accessing private members from non-friend contexts
====================== ======== =======
