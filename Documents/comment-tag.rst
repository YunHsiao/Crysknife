..
   SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
   SPDX-License-Identifier: MIT

.. _Formatting:

Comment Tag Formatting
======================

It is not uncommon for projects to have in-house coding standards,
especially wrt. modifications inside the engine source folder,
there are usually some special marks, i.e. Comment tags.

Crysknife handles this in :ref:`Config`, through :ref:`Builtin`:

Here's an example of comment tag format customization:

.. code-block:: ini

   [Variables]
   ; Enable this config file iff we are inside internal engine repos
   CRYSKNIFE_LOCAL_CONFIG_PREDICATE=@Predicate(TargetExists:Path/To/Your/Internal/Repo/Specific/File.h)

   ; Default author if not available, should override this in each plugin
   YOUR_AWESOME_ENGINE_MODULE_AUTHOR=unknown

   ; Be more lenient on spaces
   CRYSKNIFE_COMMENT_TAG_PREFIX_RE=OurAwesomeEngine:\s+
   ; Capture local author & save it along the patch
   CRYSKNIFE_COMMENT_TAG_SUFFIX_RE=:\[(?<Capture0>\w+)\]
   CRYSKNIFE_COMMENT_TAG_BEGIN_RE=:\[BEGIN\]
   CRYSKNIFE_COMMENT_TAG_END_RE=:\[END\]

   ; Always reconstruct to one space
   CRYSKNIFE_COMMENT_TAG_PREFIX_CTOR="OurAwesomeEngine: "
   ; Reconstruct the author if captured, or fallback to default
   CRYSKNIFE_COMMENT_TAG_SUFFIX_CTOR=:[${Capture0|YOUR_AWESOME_ENGINE_MODULE_AUTHOR}]
   CRYSKNIFE_COMMENT_TAG_BEGIN_CTOR=:[BEGIN]
   CRYSKNIFE_COMMENT_TAG_END_CTOR=:[END]

The ``RE`` configs are regular expression patterns when matching existing code bases.
The ``CTOR`` configs are values used to re-construct the comment tag from patches.
You can specify custom captures with explicit group name ``Capture${Index}``, and reference them accordingly in reconstructors.

A good place to put these is inside ``Crysknife/BaseCrysknife${YourEngineName}Local.ini``,
which is effective across all plugins inside the same repo.

Besides, note the ``CRYSKNIFE_LOCAL_CONFIG_PREDICATE`` variable,
which will only enable this whole config file inside when operating inside internal repo,
i.e. any other linked stock repos can keep the default comment tag format.
In fact with this mechanism you can customize any config you need on a repo-to-repo basis.

Also if you have engine-specific code inside your plugin folder, remember to guard them with macros
which can be defined in plugin local configs (``SourcePatch/Crysknife${YourEngineName}Local.ini``),
whose name must match with the relevant base config. Plugin local configs are active iff
the base config with the same name is active.

.. note::
   For UBT to cope with the above usages under :ref:`Iteration` environment, Crysknife generates
   an engine-specific config cache directly at ``Plugins/CrysknifeCache.ini``,
   which should always be commited to your internal engine repo.
