// SPDX-FileCopyrightText: 2024 Yun Hsiao Wu <yunhsiaow@gmail.com>
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using NiceIO;

namespace Modules.Crysknife
{
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

        public ConfigFileSection(string Name)
        {
            this.Name = Name;
        }
    }

    internal class ConfigFile
    {
        private static void ReadIntoSections(NPath Location, IDictionary<string, ConfigFileSection> Sections,
            ConfigLineAction DefaultAction)
        {
            using StreamReader Reader = new(Location.MakeAbsolute().ToString());
            ConfigFileSection CurrentSection = null;

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
                        TryAddConfigLine(CurrentSection, Line, StartIdx, EndIdx, DefaultAction);
                    }

                    // Otherwise just ignore it
                    break;
                }
            }
        }

        private static void TryAddConfigLine(ConfigFileSection Section,
            string Line, int StartIdx, int EndIdx, ConfigLineAction DefaultAction)
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
                Action = Line[KeyStartIdx] == '+' ? ConfigLineAction.Add :
                    Line[KeyStartIdx] == '!' ? ConfigLineAction.RemoveKey : ConfigLineAction.RemoveKeyValue;
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

            Section.Lines.Add(new ConfigLine(Action, Key, Value));
        }

        private readonly Dictionary<string, ConfigFileSection>
            Sections = new(StringComparer.InvariantCultureIgnoreCase);

        public ConfigFile(NPath Location, ConfigLineAction DefaultAction = ConfigLineAction.Set)
        {
            if (Location.Exists()) ReadIntoSections(Location, Sections, DefaultAction);
        }

        public bool TryGetSection(string SectionName, [NotNullWhen(true)] out ConfigFileSection RawSection)
        {
            return Sections.TryGetValue(SectionName, out RawSection);
        }
    }
}
