@echo off
cd %~dp0Source
dotnet build >NUL
dotnet run -- %*
