@rem SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
@rem SPDX-License-Identifier: MIT

@echo off
cd %~dp0Source

for %%x in (%*) do (
    if "%%x" == "--skip-build" (
        set Skip=true
    )
)

if "%Skip%" == "" dotnet build -c Release > NUL
"./bin/Release/net6.0/Crysknife" %*
