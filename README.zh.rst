..
    SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
    SPDX-License-Identifier: MIT

Crysknife
=========

|Doc| |Dotnet|

.. |Doc| image:: https://readthedocs.org/projects/crysknife/badge/?version=latest
   :target: https://crysknife.readthedocs.io/en/latest/?badge=latest
   :alt: Documentation Status

.. |Dotnet| image:: https://github.com/YunHsiao/Crysknife/actions/workflows/dotnet.yml/badge.svg
   :target: https://github.com/YunHsiao/Crysknife/actions/workflows/dotnet.yml
   :alt: Build Status

当在 Unreal® 引擎中实现具有一定复杂度的引擎层特性时，如果不多加考虑架构问题的话，
最后的实现往往是完全分散在引擎的各个模块，只能适用于项目内部自己维护的固定版本的引擎仓库，
而难以移植到任何其他版本，或其他项目的引擎中。

而即使是非常小心地基于引擎的扩展 (Plugin) 系统，精心设计模块结构后，
由于引擎架构的限制，往往很难，或完全无法保证不修改任何引擎源码。

最后往往作者不得不随扩展发布一个 Git Bundle, 或 Diff Patch 等这类能储存引擎 Patch 的文件，
才能将模块功能部署到其他项目的引擎仓库中。由于不同代码库间的天然差异，这个过程对终端用户来说，非常繁琐易错。

本项目可以帮助这类扩展作者从开发到部署全程管理&自动化整个“代码注入”的流程。

以下为相比手动打包的简单方案，项目的主要贡献方向：

明确的语法规则
    所有引擎改动都需要遵循明确的语法要求，保证可逆，也无需做任何手动版本管理即可自动处理 Patch, 是后续所有功能的基础。

高效的冲突处理
    Patch 应用过程为可定制阈值的模糊匹配，可以透明地处理大多数细节差异。当冲突实际发生，作为兜底，会导出一份关于冲突的 HTML 详细报告以辅助人工处理。

精准的匹配管理
    使用装饰器语法可以 **逐 Patch** 精准调整匹配上下文，细粒度控制可以大幅提高匹配过程的鲁棒性。

强大的配置系统
    即使是同一个技术产品，不同的项目也可能会有不同的使用需求，配置系统提供了多样的控制能力，可以方便地定制整个流程。
    比如根据用户配的开关选择性 Patch, 或全自动格式化 Patch 注释桩为项目内部代码规范等。

无缝的多边迭代
    项目提供多扩展架构下，跨多个引擎仓库的无缝支持。所有上述功能都可以在这样的多边环境下正常运作。
    所有 Patch 会根据注释桩信息自动检索分类到各归属扩展，扩展作者可以舒适地在任意仓库开始迭代。

从这里开始阅读文档：

https://crysknife.readthedocs.io/zh-cn/latest/usage.html
