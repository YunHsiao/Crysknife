..
   SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
   SPDX-License-Identifier: MIT

Multilateral Iteration
======================

Here is the recommended setup & workflow for iterating between multiple engines:

.. image:: iteration.png

We recommend maintain your plugins in at least two engine versions: the latest release version,
and the minimum stock engine you want to support (typically ``4.27``),
and of course your in-house engine, if there is one.

**Link** all the relevant plugin folders into every engine repositories you want to port to,
this way only one set of plugin repositories is maintained, which greatly simplified the workflow.

First, setup a multi-root workspace as follows could really help: (Using VS Code as an example)

.. code-block:: json

   {
      "folders": [
         {
            "name": "Release",
            "path": "C:/UnrealEngine-release/Engine/Source"
         },
         {
            "name": "4_27",
            "path": "C:/UnrealEngine-4-27/Engine/Source"
         },
         {
            "name": "Internal",
            "path": "C:/UnrealEngine-internal/Engine/Source"
         },
         {
            "path": "C:/UnrealEngine-release/Engine/Plugins/Crysknife"
         },
         {
            "path": "C:/UnrealEngine-release/Engine/Plugins/YourPlugin"
         },
         // ... Other dependent plugins, if any
      ],
      "settings": {
         "dotnet.defaultSolution": "${workspaceFolder:Crysknife}/Crysknife.sln",
      },
      "tasks": {
         "version": "2.0.0",
         "tasks": [
            {
               "type": "shell",
               "command": "dotnet build ${workspaceFolder:Crysknife}/Crysknife",
               "label": "build crysknife"
            }
         ]
      },
      "launch": {
         "version": "0.2.0",
         "configurations": [
            {
               "name": "Debug Crysknife",
               "type": "coreclr",
               "request": "launch",
               "preLaunchTask": "build crysknife",
               "program": "${workspaceFolder:Crysknife}/Crysknife/bin/Debug/net6.0/Crysknife.dll",
               "cwd": "${workspaceFolder:Crysknife}",
               "stopAtEntry": false,
               "console": "internalConsole",
               "args": [
                  "-P", "YourPlugin", "-E",

                  "${workspaceFolder:Release}/../..",
                  "-G",

                  // "${workspaceFolder:4_27}/../..",
                  // "-Gn",

                  // "${workspaceFolder:Internal}/../..",
                  // "-Gn", "2",

                  // "-i", "StaticMeshComponentLODInfo",
               ],
            },
         ],
      },
   }

The setup script is written carefully with this kind of environment in mind,
you can just run the same script from different linked directory and it will update the parent repository accordingly.

Say we just finished developing for ``Release``, now want to port to ``4.27``:

.. code-block:: bash

   # Generate patches from Release, apply to 4.27

   ${workspaceFolder:Release}/../Plugins/YourPlugin/Setup.sh -G
   ${workspaceFolder:4_27}/../Plugins/YourPlugin/Setup.sh

Then switch to ``4.27`` and start resolving conflicts & do the actual porting. After finished:

.. code-block:: bash

   # Generate patches from 4.27, apply to release

   # Using incremental generation that preserves the history patch
   # if it deemed equal and not specific to current engine version
   ${workspaceFolder:4_27}/../Plugins/YourPlugin/Setup.sh -Gn
   ${workspaceFolder:Release}/../Plugins/YourPlugin/Setup.sh

This way the patches are updated incrementally, making it much easier and focused to sync back to the ``Release`` repo.
It may take some back-and-forth efforts, but do make sure the same set of patches are up-to-date for both engines,
which is critical for a smooth deployment experience.

Finally, when the porting is done, the same process still applies **anywhere anytime** changes are made.
Say we just fixed a rare corner case found in internal repo, to properly commit the changes, follow these steps:

.. code-block:: bash

   # Generate patches from internal, sync with all others

   # Incremental generation that preserves the history patch if it deemed equal
   ${workspaceFolder:Internal}/../Plugins/YourPlugin/Setup.sh -Gn 2

   # Apply to release & sync patches
   ${workspaceFolder:Release}/../Plugins/YourPlugin/Setup.sh -AG

   # Apply to 4.27 & sync patches
   ${workspaceFolder:4_27}/../Plugins/YourPlugin/Setup.sh -AGn

   # Sync back to internal, to make sure everything is up-to-date
   ${workspaceFolder:Internal}/../Plugins/YourPlugin/Setup.sh

   # Now you are ready submit new changes for all relevant plugins

It is recommended to use the ``Release`` repo as the base patch source,
incrementally update the patches from other stock versions,
and never **commit** patches from internal repo (temporary updates like above are fine).
