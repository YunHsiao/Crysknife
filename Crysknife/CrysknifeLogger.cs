// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

namespace Crysknife;

public enum LogLevel
{
    Verbose,
    Info,
    Action,
    Warning,
    Error,
    Fatal,
}

public interface ILogger
{
    void Log(LogLevel Level, string Format, params object[] Args);
}

public static class Logger
{
    private static ILogger Instance = new ConsoleLogger();

    public static void SetLogger(ILogger NewLogger)
    {
        Instance = NewLogger;
    }

    public static void Verbose(string Format, params object[] Args) => Instance.Log(LogLevel.Verbose, Format, Args);
    public static void Info(string Format, params object[] Args) => Instance.Log(LogLevel.Info, Format, Args);
    public static void Action(string Format, params object[] Args) => Instance.Log(LogLevel.Action, Format, Args);
    public static void Warning(string Format, params object[] Args) => Instance.Log(LogLevel.Warning, Format, Args);
    public static void Error(string Format, params object[] Args) => Instance.Log(LogLevel.Error, Format, Args);
    public static void Fatal(string Format, params object[] Args) => Instance.Log(LogLevel.Fatal, Format, Args);
}

internal class ConsoleLogger : ILogger
{
    private static readonly Dictionary<LogLevel, (ConsoleColor Color, string Prefix)> LevelStyles = new()
    {
        { LogLevel.Verbose, (ConsoleColor.Gray,     "[VRB]") },
        { LogLevel.Info,    (ConsoleColor.Blue,     "[INF]") },
        { LogLevel.Action,  (ConsoleColor.Green,    "[ACT]") },
        { LogLevel.Warning, (ConsoleColor.Yellow,   "[WRN]") },
        { LogLevel.Error,   (ConsoleColor.Red,      "[ERR]") },
        { LogLevel.Fatal,   (ConsoleColor.DarkRed,  "[FTL]") },
    };

    public void Log(LogLevel Level, string Format, params object[] Args)
    {
        var (Color, Prefix) = LevelStyles[Level];
        var Message = Args.Length > 0 ? string.Format(Format, Args) : Format;

        Console.ForegroundColor = Color;
        var Output = Level >= LogLevel.Error ? Console.Error : Console.Out;
        Output.WriteLine("{0} {1}", Prefix, Message);
        Console.ResetColor();
    }
}
