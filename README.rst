..
 SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
 SPDX-License-Identifier: MIT

Crysknife
=========

.. image:: https://readthedocs.org/projects/crysknife/badge/?version=latest
   :target: https://crysknife.readthedocs.io/en/latest/?badge=latest
   :alt: Documentation Status

When implementing complex engine-level customizations for Unreal® Engine,
oftentimes the changes are completely scattered across engine modules,
which could be fine for one in-house engine base, but extremely hard to port to any other.

Even with extended care trying to organize everything into an engine plugin,
due to many design decisions of the engine architecture, it is very hard,
if not impossible to completely keep away from modifying stock engine files to make everything fully work.

In the end the plugin authors have to ship along some kind of *patch* file
with the necessary *surgical* modifications into the engine. Be it a git bundle, a diff patch, etc.
to be able to deploy their features to other engine repositories. Due to natural differences between code bases,
to put it lightly, it does not scale well.

This project aims to provide all the necessary means to oversee & automate this process
throughout the development & deployment phase.

Here are the major benefits compared to hand-rolled general-purpose patch files:

Explicit Syntax Requirements
 All modifications must follow a set of syntax rules, which strictly guarantees reciprocity
 and does not require any version control to proceed.

Efficient Conflict Resolution
 Patches are fuzzy-matched with customizable thresholds, which eliminates most of the trivial
 differences. When conflicts do arise, as the bottom line, a detailed report on the failing patch
 will be exported as an HTML file to guide manual resolution.

Precise Context Management
 Matching context can be fully customized **per-patch** with the decorator system, which
 provides the much-needed fine-grained control, greatly increasing the robustness of the matching process.

Powerful Config System
 Different projects may have different needs for the same plugin, the config system provides a wide range
 of capabilities to control how the patches should be processed. e.g. Conditional patching based on
 required feature switches, or transparently format the comment tag to conforming in-house coding standards.

Seamless Multilateral Iteration
 The project provide seamless support for multi-plugin architecture,
 across multiple engine repositories. Everything above just works under this kind of multilateral environment.
 Patches will be categorized & processed based on the comment tag from/to each plugin folder automatically.
 Plugin authors can start iterating comfortably from any repository of interest.

To get started, follow the documents here:

https://crysknife.readthedocs.io/en/latest/usage.html
