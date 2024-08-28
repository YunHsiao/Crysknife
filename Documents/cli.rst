..
   SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
   SPDX-License-Identifier: MIT

Command Line Interface
======================

-P <PLUGIN>
   The input plugin folder name (by default also as the comment guard tag). Always required.
-E <ENGINE_ROOT>
   The engine root directory, default to the engine in which this repo is located.
-D <VAR=VALUE...>
   Define custom config variables.

Actions
-------

-S
   Setup the specified plugin with proper configs & scripts, run once per plugin
-R <FILES|DIRECTORIES...>
   Search specified file or directory, register any patched file and generate
-U <FILES|DIRECTORIES...>
   Search specified file or directory, unregister any patched file and update
-G
   Generate/update patches
-C
   Clear patches from target files
-A
   Apply existing patches and copy all new sources (default action)

Modifiers
---------

-i <FILTER>, --inclusive-filter <FILTER>
   Inclusive target path filter for all actions
-e <FILTER>, --exclusive-filter <FILTER>
   Exclusive target path filter for all actions
-n <LEVEL?>, --incremental <LEVEL?>
   Update patches incrementally based on existing patch status
-l, --link
   Make symbolic links instead of copying all the new files
-f, --force
   Force override existing files
-d, --dry-run
   Test run, safely executes the action with all engine output remapped
   to the plugin's ``Intermediate/Crysknife/Playground`` directory
-v, --verbose
   Log more verbosely about everything
-p, --protected
   Patches will be saved to / loaded from protected sources which will not be committed
-t, --treat-patch-as-file
   Treat patches as regular files, copy/link them directly

Parameters
----------

--patch-context <LENGTH>
   Global patch context length when generating patches, defaults to 250
--content-tolerance <TOLERANCE>
   Content tolerance in [0, 1] when matching sources, default to 0.3
--line-tolerance <TOLERANCE>
   Line tolerance when matching sources, defaults to infinity
   (line numbers may vary significantly between engine versions)

.. note::
   Actions are combinatorial:

   e.g. ``-GA`` for generate & apply (round trip), ``-GC`` for generate & clear (retraction)
