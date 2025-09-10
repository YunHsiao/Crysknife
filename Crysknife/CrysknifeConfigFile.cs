// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT
// Based on Engine/Source/Programs/UnrealBuildTool/System/ConfigFile.cs

using System.Diagnostics.CodeAnalysis;

namespace Crysknife;

internal enum ConfigLineAction
{
    Set,
    Add,
    RemoveKey,
    RemoveKeyValue
}

internal class ConfigLine
{
    public readonly ConfigLineAction Action;
    public readonly string Key;
    public readonly string Value;

    public ConfigLine(ConfigLineAction Action, string Key, string Value)
    {
        this.Action = Action;
        this.Key = Key;
        this.Value = Value;
    }
    public override string ToString()
    {
        var Prefix = Action switch
        {
            ConfigLineAction.Add => "+",
            ConfigLineAction.RemoveKey => "!",
            ConfigLineAction.RemoveKeyValue => "-",
            _ => ""
        };
        return $"{Prefix}{Key}={Value}";
    }
}

internal class ConfigFileSection
{
    public readonly string Name;
    public readonly List<ConfigLine> Lines = new();

    public void ParseLines(IDictionary<string, string> Result, char Separator, Func<string, string>? MapFunc = null)
    {
        Result.Clear();
        foreach (var Line in Lines)
        {
            var Value = MapFunc != null ? MapFunc(Line.Value) : Line.Value;
            switch (Line.Action)
            {
                case ConfigLineAction.Set:
                    Result[Line.Key] = Value;
                    break;
                case ConfigLineAction.Add:
                    Result.TryGetValue(Line.Key, out var Current);
                    Result[Line.Key] = string.Join(Separator, Current, Value);
                    break;
                case ConfigLineAction.RemoveKey:
                    Result.Remove(Line.Key);
                    break;
                case ConfigLineAction.RemoveKeyValue:
                    Result.TryGetValue(Line.Key, out Current);
                    if (Current != null) Result[Line.Key] = Current.Replace(Value, string.Empty);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public ConfigFileSection(string Name)
    {
        this.Name = Name;
    }
}

internal class ConfigFile
{
    private static string RemapSectionOrKey(IDictionary<string, string>? Remap, string Key, string Context)
    {
        if (Remap == null) return Key;
        if (!Remap.TryGetValue(Key, out var Remapped)) return Key;
        if (!WarnedKeys.Add(Key)) return Remapped;

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"DEPRECATION: '{Key}', {Context}, has been deprecated. Using '{Remapped}' instead. It is recommended you update your .ini files as soon as possible, and replace {Key} with {Remapped}");
        return Remapped;
    }

    private static void ReadIntoSections(string Location, IDictionary<string, ConfigFileSection> Sections, ConfigLineAction DefaultAction)
    {
        using StreamReader Reader = new (Location);
        ConfigFileSection? CurrentSection = null;
        Dictionary<string, string>? CurrentRemap = null;

        while (true)
        {
            var Line = Reader.ReadLine();
            if (Line == null)
            {
                break;
            }

            // Find the first non-whitespace character
            for (var StartIdx = 0; StartIdx < Line.Length; StartIdx++)
            {
                if (Line[StartIdx] == ' ' || Line[StartIdx] == '\t') continue;

                // Find the last non-whitespace character. If it's an escaped newline, merge the following line with it.
                var EndIdx = Line.Length;
                while (EndIdx > StartIdx)
                {
                    if (Line[EndIdx - 1] == '\\')
                    {
                        var NextLine = Reader.ReadLine();
                        if (NextLine == null)
                        {
                            break;
                        }
                        Line += NextLine;
                        EndIdx = Line.Length;
                        continue;
                    }
                    if (Line[EndIdx - 1] != ' ' && Line[EndIdx - 1] != '\t')
                    {
                        break;
                    }

                    EndIdx--;
                }

                // Break out if we've got a comment
                if (Line[StartIdx] == ';')
                {
                    break;
                }
                if (Line[StartIdx] == '/' && StartIdx + 1 < Line.Length && Line[StartIdx + 1] == '/')
                {
                    break;
                }

                // Check if it's the start of a new section
                if (Line[StartIdx] == '[')
                {
                    CurrentSection = null;
                    if (Line[EndIdx - 1] == ']')
                    {
                        var SectionName = Line.Substring(StartIdx + 1, EndIdx - StartIdx - 2);

                        // lookup remaps
                        SectionName = RemapSectionOrKey(SectionNameRemap, SectionName, $"which is a config section in '{Location}'");
                        SectionKeyRemap.TryGetValue(SectionName, out CurrentRemap);

                        if (!Sections.TryGetValue(SectionName, out CurrentSection))
                        {
                            CurrentSection = new ConfigFileSection(SectionName);
                            Sections.Add(SectionName, CurrentSection);
                        }
                    }
                    break;
                }

                // Otherwise add it to the current section or add a new one
                if (CurrentSection != null)
                {
                    TryAddConfigLine(CurrentSection, CurrentRemap, Location, Line, StartIdx, EndIdx, DefaultAction, Sections);
                }

                // Otherwise just ignore it
                break;
            }
        }
    }

    private static void TryAddConfigLine(ConfigFileSection Section, IDictionary<string, string>? KeyRemap, string Filename,
        string Line, int StartIdx, int EndIdx, ConfigLineAction DefaultAction, IDictionary<string, ConfigFileSection> Sections)
    {
        // Find the '=' character separating key and value
        var EqualsIdx = Line.IndexOf('=', StartIdx, EndIdx - StartIdx);
        if (EqualsIdx == -1 && Line[StartIdx] != '!')
        {
            return;
        }

        // Keep track of the start of the key name
        var KeyStartIdx = StartIdx;

        // Remove the +/-/! prefix, if present
        var Action = DefaultAction;
        if (Line[KeyStartIdx] == '+' || Line[KeyStartIdx] == '-' || Line[KeyStartIdx] == '!')
        {
            Action = Line[KeyStartIdx] == '+' ? ConfigLineAction.Add : Line[KeyStartIdx] == '!' ? ConfigLineAction.RemoveKey : ConfigLineAction.RemoveKeyValue;
            KeyStartIdx++;
            while (Line[KeyStartIdx] == ' ' || Line[KeyStartIdx] == '\t')
            {
                KeyStartIdx++;
            }
        }

        // RemoveKey actions do not require a value
        if (Action == ConfigLineAction.RemoveKey && EqualsIdx == -1)
        {
            Section.Lines.Add(new ConfigLine(Action, Line[KeyStartIdx..].Trim(), ""));
            return;
        }

        // Remove trailing spaces after the name of the key
        var KeyEndIdx = EqualsIdx;
        for (; KeyEndIdx > KeyStartIdx; KeyEndIdx--)
        {
            if (Line[KeyEndIdx - 1] != ' ' && Line[KeyEndIdx - 1] != '\t')
            {
                break;
            }
        }

        // Make sure there's a non-empty key name
        if (KeyStartIdx == EqualsIdx)
        {
            return;
        }

        // Skip whitespace between the '=' and the start of the value
        var ValueStartIdx = EqualsIdx + 1;
        for (; ValueStartIdx < EndIdx; ValueStartIdx++)
        {
            if (Line[ValueStartIdx] != ' ' && Line[ValueStartIdx] != '\t')
            {
                break;
            }
        }

        // Strip quotes around the value if present
        var ValueEndIdx = EndIdx;
        if (ValueEndIdx >= ValueStartIdx + 2 && Line[ValueStartIdx] == '"' && Line[ValueEndIdx - 1] == '"')
        {
            ValueStartIdx++;
            ValueEndIdx--;
        }

        // Add it to the config section
        var Key = Line.Substring(KeyStartIdx, KeyEndIdx - KeyStartIdx);
        var Value = Line.Substring(ValueStartIdx, ValueEndIdx - ValueStartIdx);

        // Remap the key if needed
        var NewKey = RemapSectionOrKey(KeyRemap, Key, $"which is a config key in section [{Section.Name}], in '{Filename}'");

        // Look for a section:name remap
        if (!NewKey.Equals(Key) && NewKey.Contains(':'))
        {
            var SectionName = NewKey[..NewKey.IndexOf(':')];
            if (!Sections.TryGetValue(SectionName, out var CurrentSection))
            {
                CurrentSection = new ConfigFileSection(SectionName);
                Sections.Add(SectionName, CurrentSection);
            }

            var KeyName = NewKey[(NewKey.IndexOf(':') + 1)..];
            CurrentSection.Lines.Add(new ConfigLine(Action, KeyName, Value));

            return;
        }

        Section.Lines.Add(new ConfigLine(Action, NewKey, Value));
    }

    private ConfigFileSection FindOrAddSection(string SectionName)
    {
        if (Sections.TryGetValue(SectionName, out var Section)) return Section;

        Section = new ConfigFileSection(SectionName);
        Sections.Add(SectionName, Section);
        return Section;
    }

    private readonly Dictionary<string, ConfigFileSection> Sections = new (StringComparer.InvariantCultureIgnoreCase);

    // Remap of config names/sections
    private static readonly Dictionary<string, string> SectionNameRemap = new();
    private static readonly Dictionary<string, Dictionary<string, string>> SectionKeyRemap = new();
    private static readonly HashSet<string> WarnedKeys = new (StringComparer.InvariantCultureIgnoreCase);

    public static void Init(string RootDirectory)
    {
        Dictionary<string, ConfigFileSection> Sections = new (StringComparer.InvariantCultureIgnoreCase);
        try
        {
            // Read the special ConfigRedirects.ini file into sections
            var ConfigRemapFile = Path.Combine(RootDirectory, "ConfigRedirects.ini");
            if (File.Exists(ConfigRemapFile))
            {
                ReadIntoSections(ConfigRemapFile, Sections, ConfigLineAction.Set);
            }
        }
        catch (Exception)
        {
            // Make ConfigFile when EngineDirectory is unknown a warning since ConfigRemapFile cannot be read in this case; e.g. Assemblies outside Engine that depend on ConfigFile
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Failed to read ConfigRemapFile into Sections");
        }

        // Walk over the sections, where all but the special SectionNameRemap section is a section of keys to remap in that same section
        foreach (var Pair in Sections)
        {
            // Remember a remap for section names
            if (Pair.Key.Equals("SectionNameRemap", StringComparison.InvariantCultureIgnoreCase))
            {
                foreach (var Line in Pair.Value.Lines)
                {
                    SectionNameRemap.Add(Line.Key, Line.Value);
                }
            }
            else
            {
                // Any other section is remembered by the section name here, and each key/value pair is a remap for the given section
                Dictionary<string, string> KeyRemap = new (StringComparer.InvariantCultureIgnoreCase);
                SectionKeyRemap.Add(Pair.Key, KeyRemap);
                foreach (var Line in Pair.Value.Lines)
                {
                    KeyRemap.Add(Line.Key, Line.Value);
                }
            }
        }
    }

    public ConfigFile() {}

    public ConfigFile(string Location, ConfigLineAction DefaultAction = ConfigLineAction.Set)
    {
        if (File.Exists(Location)) ReadIntoSections(Location, Sections, DefaultAction);
    }

    public ConfigFile Merge(ConfigFile File, bool AppendToTail = true)
    {
        foreach (var SectionName in File.SectionNames)
        {
            if (!File.TryGetSection(SectionName, out var BaseSection)) continue;

            if (AppendToTail)
            {
                FindOrAddSection(SectionName).Lines.AddRange(BaseSection.Lines);
            }
            else
            {
                FindOrAddSection(SectionName).Lines.InsertRange(0, BaseSection.Lines);
            }
        }

        return this;
    }

    public void AppendFromText(string SectionName, string IniText, ConfigLineAction DefaultAction = ConfigLineAction.Set)
    {
        foreach (var Setting in IniText.Split(',', Utils.SplitOptions))
        {
            SectionKeyRemap.TryGetValue(SectionName, out var CurrentRemap);

            if (!Sections.TryGetValue(SectionName, out var CurrentSection))
            {
                CurrentSection = new ConfigFileSection(SectionName);
                Sections.Add(SectionName, CurrentSection);
            }

            TryAddConfigLine(CurrentSection, CurrentRemap, "unknown source file", Setting, 0, Setting.Length, DefaultAction, Sections);
        }
    }

    public IEnumerable<string> SectionNames => Sections.Keys;

    public bool TryGetSection(string SectionName, [NotNullWhen(true)] out ConfigFileSection? RawSection)
    {
        return Sections.TryGetValue(SectionName, out RawSection);
    }
}
