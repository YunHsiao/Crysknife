// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

namespace Crysknife;

public static class SetupScripts
{
    private static readonly string WindowsTemplate = @"
        @echo off
        ""%~dp0..\Crysknife\Crysknife.bat"" -p {0} %*
        pause
    ".Replace("    ", string.Empty);

    private static readonly string LinuxTemplate = @"
        DIR=`cd ""$(dirname ""$0"")""; pwd`
        ""$DIR/../Crysknife/Crysknife.sh"" -p {0} ""$@""
    ".Replace("    ", string.Empty).Replace("\r\n", "\n");

    public static void Generate(string RootDirectory, string ProjectName)
    {
        string TargetDirectory = Path.Combine(RootDirectory, ProjectName);
        File.WriteAllText(Path.Combine(TargetDirectory, "Setup.bat"), string.Format(WindowsTemplate, ProjectName));
        File.WriteAllText(Path.Combine(TargetDirectory, "Setup.sh"), string.Format(LinuxTemplate, ProjectName));
        File.WriteAllText(Path.Combine(TargetDirectory, "Setup.command"), string.Format(LinuxTemplate, ProjectName));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Setup scripts created: " + Path.Combine(TargetDirectory, "Setup"));
    }
}
