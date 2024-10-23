..
   SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
   SPDX-License-Identifier: MIT

.. _Config:

Config System
=============

Every ``SourcePatch`` directory can have one config file ``Crysknife.ini`` at the root,
to specify more complex patching behaviors such as conditional remapping, etc. in the following framework:


.. code-block:: ini

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
   ; Multiple conditions are allowed, separated by vertical bar
   Rule1=Predicate1:Value1|Value2
   ; Or add them in separate lines
   +Rule1=Predicate1:Value3
   ; Variable references & reverse dependencies
   ; Multiple predicates are separated with comma
   +Rule1=Predicate3:${Var1},Predicate4:!Value5

   [Path/To/Dir1]
   ; Only apply to the specified subdirectory
   ScopedRule1=Predicate2

   ; This scope automatically extends from the above parent scope
   [Path/To/Dir1/Folder1]
   ScopedRule2=Predicate5

   ; Multiple directories are allowed
   [Path1|Path2]

The global section applies the rule to all subdirectories inside the ``SourcePatch`` folder,
while custom sections with relative file/directory paths can be used to limit the effective scope of the rules.

Multiple subdirectories can be specified within one section title,
separated with ``|``. Scoped rules will apply to all subdirectories.

If multiple sections affect the same file, the inner (path) section will automatically extend from the outer section.

The variables section declares custom variables that can be referenced with ``${VariableName}`` in any value.

Any value can be preceded by `!` to indicate a reverse predicate (satisfies if the condition is not met).

The dependencies section declares relevant source patches inside other plugins,
which can provide seamless support when source patches are separated into multiple plugins.

.. note::
   You can also create 'local' config files (``CrysknifeLocal.ini``) that overrides the main one.

   By design it is ignored in your plugin's git repo, but should be committed into the main engine (e.g. perforce) code base.

   This is useful to specify engine-specific configs like comment tag format, etc.

Supported Rules
---------------

``SkipIf=<PREDICATE>...``
   Skip action if specified predicates are satisfied

``RemapIf=<PREDICATE>...``
  Remap action to a different destination directory if specified predicates are satisfied

``RemapTarget=<PATH>``
   The remap destination, which would be replacing the section name part of the input file path
   Must be specified if ``RemapIf`` is present

``FlattenIf=<PREDICATE>...``
   Flatten the folder hierarchy if specified predicates are satisfied

``^Base<RULE>=...``
   Add a ``Base`` prefix to rule name to indicate the current rule line is in the base domain and,
   therefore can only be overruled by the same domain directives
   Use this iff you want to override base rules defined in `BaseCrysknife.ini`_

.. _BaseCrysknife.ini: https://github.com/YunHsiao/Crysknife/blob/main/BaseCrysknife.ini

Supported Predicates
--------------------

``TargetExists:<FILE|DIRECTORY>...``
   Satisfies if any of the specified files/directories exists

``IsTruthy:<SWITCH>...``
   Satisfies if any of the specified values is true

``NameMatches:<NAME>...``
   Satisfies if the input file name matches

``NewerThan:<VERSION>...``
   Satisfies if current engine version is newer than or equal to specified value (e.g. ``5.0``)

``Conjunction``
   By default all conditions are disjunction, i.e. the results are logically OR-ed together.
   ``Conjunction`` can be specified at rule-level, or inside predicates
   to change the corresponding logical behavior to conjunction.

``Always``, ``Never``
   Always / never satisfies

Variable Perks
--------------

``${<VAR_NAME>|<?FALLBACK_VAR_NAME>}``
   Substitutes with the value of the specified variable, or the optional fallback variable if not found.

``@Predicate(<PREDICATE>...)``
   Evaluates to the result of the specified predicate

``#<VARIABLE_NAME>``
   Prefix variable name with ``#`` to make it 'local', i.e. environment-specific,
   thus will not be included in config cache files. The prefix should be omitted when referencing.

.. _Builtin:

Built-in Variables
------------------

``CRYSKNIFE_ENGINE_ROOT``
   Full path to the ``Engine`` folder, readonly
``CRYSKNIFE_PLUGIN_DIRECTORY``
   Full path to the target plugin directory, readonly
``CRYSKNIFE_SOURCE_DIRECTORY``
   Full path to the engine source directory, readonly
``CRYSKNIFE_COMMENT_TAG``
   Default to the plugin folder name, you can assign a more distinctive name if needed
``CRYSKNIFE_CUSTOM_COMMENT_TAG_PREDICATE``
   Enable custom comment tag format iff this predicate evaluates to true
``CRYSKNIFE_COMMENT_TAG_(PREFIX|SUFFIX|BEGIN|END)_(RE|CTOR)``
   Regex matchers & re-constructors of the comment tag
``CRYSKNIFE_COMMENT_TAG_ANASTROPHE``
   Whether to put tag & suffix after begin/end clause
``CRYSKNIFE_COMMENT_TAG_CRLF``
   Whether to use CRLF line endings for all outputs
