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
   YOUR_AWESOME_ENGINE=@Predicate(TargetExists:Path/To/Your/Internal/Repo/Specific/File.h)

   ; Default author if not available, should override this in each plugin
   YOUR_AWESOME_ENGINE_MODULE_AUTHOR=unknown

   ; Enable iff we are inside internal engine repos
   CRYSKNIFE_CUSTOM_COMMENT_TAG=${YOUR_AWESOME_ENGINE}

   ; Be more lenient on spaces
   CRYSKNIFE_CUSTOM_COMMENT_TAG_PREFIX_RE=OurAwesomeEngine:\s+
   ; Capture local author & save it along the patch
   CRYSKNIFE_CUSTOM_COMMENT_TAG_SUFFIX_RE=:\[(?<Capture0>\w+)\]
   CRYSKNIFE_CUSTOM_COMMENT_TAG_BEGIN_RE=:\[BEGIN\]
   CRYSKNIFE_CUSTOM_COMMENT_TAG_END_RE=:\[END\]

   ; Always reconstruct to one space
   CRYSKNIFE_CUSTOM_COMMENT_TAG_PREFIX_CTOR="OurAwesomeEngine: "
   ; Reconstruct the author if captured, or fallback to default
   CRYSKNIFE_CUSTOM_COMMENT_TAG_SUFFIX_CTOR=:[${Capture0|YOUR_AWESOME_ENGINE_MODULE_AUTHOR}]
   CRYSKNIFE_CUSTOM_COMMENT_TAG_BEGIN_CTOR=:[BEGIN]
   CRYSKNIFE_CUSTOM_COMMENT_TAG_END_CTOR=:[END]

The ``RE`` configs are regular expression patterns when matching existing code bases.
The ``CTOR`` configs are values used to re-construct the comment tag from patches.
You can specify custom captures with explicit group name ``Capture${Index}``, and reference them accordingly in reconstructors.

A good place to put these is inside ``Crysknife/BaseCrysknifeLocal.ini``,
which is effective across all plugins inside the same repo.

Besides, note the ``CRYSKNIFE_CUSTOM_COMMENT_TAG`` variable,
which will only enable the customization inside internal repo,
i.e. any other linked stock repos can keep the default comment tag format.
