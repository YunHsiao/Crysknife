@rem SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
@rem SPDX-License-Identifier: MIT

@echo off
cd %~dp0Source
dotnet build >NUL
dotnet run -- %*
