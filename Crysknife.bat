@rem SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
@rem SPDX-License-Identifier: MIT

@echo off
cd %~dp0Crysknife

for %%x in (%*) do (
    if "%%x" == "--skip-build" (
        set Skip=true
    )
)

if "%Skip%" == "" dotnet build -nologo -consoleLoggerParameters:NoSummary -verbosity:quiet -c Release
"./bin/Release/net8.0/Crysknife" %*
