@echo off
cd %~dp0
dotnet build >NUL
dotnet run -- %*
