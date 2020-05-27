﻿namespace ASMCreator_Optimizer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
#if !DEBUG
    using System.Diagnostics;
#endif
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    [SuppressMessage("ReSharper", "CognitiveComplexity")]
    internal static class Program
    {
        private const ConsoleColor FilePrintColor = ConsoleColor.Yellow;

        private static void PrintFile(string message, IEnumerable<string> content)
        {
            Console.WriteLine(message);

            var color = Console.ForegroundColor;
            Console.ForegroundColor = FilePrintColor;

            Console.WriteLine(string.Join(Environment.NewLine, content));
            Console.WriteLine();

            Console.ForegroundColor = color;
        }

        private static void Exit(string message)
        {
            Console.WriteLine(message);
            Console.WriteLine("Zamknij program i spróbuj jeszcze raz");
            Console.ReadKey(true);

            Environment.Exit(-1);
        }

        private static readonly List<string> Gsa, Txt, Mic;

        static Program()
        {
            Gsa = new List<string>();
            Txt = new List<string>();
            Mic = new List<string>();
        }

        private static List<string> GetListByIndex(int i) => i switch
        {
            0 => Gsa,
            1 => Txt,
            2 => Mic,
            _ => throw new IndexOutOfRangeException()
        };

        // kod jest generyczny i zadziała na windows, linux, może mac
        // wystarczy skompilować pod odpowiednią wersję systemu
        private static void Main(string[] args)
        {
            // zezwól na polskie znaki w konsoli
            Console.OutputEncoding = CodePagesEncodingProvider.Instance.GetEncoding("Windows-1250")!;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
#if DEBUG
            if (args.Length == 0)
            {
                const string DesktopFolderName = "Studio";

                var folder = Path.Combine(desktop, DesktopFolderName);

                if (!Directory.Exists(folder))
                {
                    throw new DirectoryNotFoundException($"Debug mode; Directory Desktop/{DesktopFolderName} not found; can't locate files!");
                }

                args = Directory.GetFiles(folder);
            }
#endif
            // czytaj pliki o tym rozszerzeniu z drag & drop
            // działa też z wejściem jako parametr
            var sources = Array.ConvertAll(new[] { "gsa", "txt", "mic" }, e => args.SingleOrDefault(p => Path.GetExtension(p) == $".{e}"));

            if (Array.TrueForAll(sources, s => s == null))
            {
                Exit("Przeciągnij pliki do optymalizacji na .exe lub podaj argumenty!");
            }

            if (!Array.Exists(sources, s => s.EndsWith("gsa")))
            {
                Exit("Nie podano wymaganego pliku gsa");
            }

            if (!Array.Exists(sources, s => s.EndsWith("txt")))
            {
                Exit("Nie podano wymaganego pliku txt");
            }

            for (var i = 0; i < sources.Length; ++i)
            {
                var source = sources[i];

                if (source != null)
                {
                    GetListByIndex(i).AddRange(File.ReadAllLines(source));
                }
            }

            PrintFile("WCZYTANO TXT:", Txt);
            PrintFile("WCZYTANO GSA:", Gsa);

            Console.WriteLine("PRZETWARZAM...");

            OptimizePaths();
            OptimizeVariables();

            PrintFile("GSA - PO EDYCJI:", Gsa);
            PrintFile("TXT - PO EDYCJI:", Txt);
#if DEBUG
            Console.WriteLine("GOTOWE");
            Console.ReadKey(true);
#else
            var dirs = Directory.GetDirectories(desktop, "Optimized_*");

            if (dirs.Length > 0)
            {
                foreach (var dir in dirs)
                {
                    Console.WriteLine($"Usuwam stary folder {dir}");
                    Directory.Delete(dir, true);
                }

                Console.WriteLine();
            }

            var directoryName = $"Optimized_{DateTime.Now:yyyy-dd-M--HH-mm-ss}"; // format nazwy folderu
            var directory = Path.Combine(desktop, directoryName);
            Directory.CreateDirectory(directory);

            for (var i = 0; i < sources.Length; i++)
            {
                var source = sources[i];

                if (source == null)
                {
                    continue;
                }

                var fileName = Path.GetFileName(source);
                File.WriteAllLines(Path.Combine(directory, fileName), GetListByIndex(i));

                Console.WriteLine($"Zapisano {directoryName}/{fileName}");
            }

            Process.Start(new ProcessStartInfo
                          {
                              FileName = directory,
                              Verb = "open",
                              UseShellExecute = true
                          });

            Console.WriteLine("Otwieram folder z wynikiem");
#endif
        }

        private static readonly Regex GsaRegex = new Regex(@"(\d+) ([A-Za-z0-9]+) +(\d+) +(\d+)");

        private static Match GsaMatch(string line)
        {
            var match = GsaRegex.Match(line);

            return match.Success ? match : throw new ArgumentException("Uszkodzony plik .gsa");
        }

        private static IEnumerable<Match> IterateGsa()
        {
            for (var i = 1; i <= Gsa.Count - 1; i++)
            {
                yield return GsaMatch(Gsa[i]);
            }
        }

        private static int GetId(this Match match) => int.Parse(match.Groups["1"].Value);

        private static string GetName(this Match match) => match.Groups["2"].Value;

        private static int GetFirstExit(this Match match) => int.Parse(match.Groups["3"].Value);

        private static int GetSecondExit(this Match match) => int.Parse(match.Groups["4"].Value);

        private static int GetGsaIndexForMatch(Match m)
        {
            var id = m.GetId().ToString();

            return Gsa.FindIndex(l => l.TrimStart().StartsWith(id));
        }

        private static int GetTxtIndexForName(string name)
        {
            return Txt.FindIndex(l => l.StartsWith(name));
        }

        // "y2" -> "x:=y+2"
        private static string GetOperationForInstruction(string instruction)
        {
            return ExtractOperationFromTxtLine(Txt.Find(line => line.StartsWith(instruction)));
        }

        // "Y5" -> { "x:=7", "y:=5" }
        private static IEnumerable<string> GetOperationsByName(string name)
        {
            return ExtractInstructionsFromTxtLine(Txt[GetTxtIndexForName(name)]).Select(GetOperationForInstruction);
        }

        // "Y3 = y3 y4" -> { "y3", "y4" }
        private static IEnumerable<string> ExtractInstructionsFromTxtLine(string line)
        {
            return line.Split(' ').Skip(2);
        }

        // "y1  :  y:=1"
        private static string ExtractOperationFromTxtLine(string line)
        {
            return line.Substring(line.IndexOf(':') + 1).TrimStart();
        }

        // "y:=x1+2" -> { "y", "x1" }
        private static IEnumerable<string> ExtractVariablesFromOperation(string operation)
        {
            var regex = new Regex("[a-z][a-z0-9]*");

            return from match in regex.Matches(operation) select match.Value;
        }

        private static void UpdateGsaId(int oldId, int newId)
        {
            foreach (var match in IterateGsa().Where(match => match.GetFirstExit() == oldId || match.GetSecondExit() == oldId))
            {
                var index = GetGsaIndexForMatch(match);
                var line = Gsa[index];

                var name = match.GetName();
                var length = line.IndexOf(name, StringComparison.InvariantCulture) + name.Length;

                Gsa[index] = line.Substring(0, length) + line.Substring(length).Replace(oldId.ToString(), newId.ToString());
            }
        }

        private static void RemoveInstruction(string instruction)
        {
            var oldIndex = Gsa.FindIndex(l => l.Contains(instruction));
            var oldMatch = GsaMatch(Gsa[oldIndex]);
            var oldId = oldMatch.GetId();
            var newId = oldMatch.GetFirstExit();

            Gsa.RemoveAt(oldIndex);

            // połącz z elementem zastępującym
            UpdateGsaId(oldId, newId);

            for (var i = 2; i < Gsa.Count; i++)
            {
                var match = GsaMatch(Gsa[i]);

                oldId = match.GetId();
                newId = GsaMatch(Gsa[i - 1]).GetId() + 1;

                if (oldId != newId)
                {
                    var oldIdStr = oldId.ToString();
                    var line = Gsa[i];
                    var length = line.IndexOf(oldIdStr, StringComparison.InvariantCulture) + oldIdStr.Length;

                    Gsa[i] = line.Substring(0, length).Replace(oldIdStr, newId.ToString()) + line.Substring(length);

                    UpdateGsaId(oldId, newId);
                }
            }

            Gsa[0] = Gsa[0].Replace(oldId.ToString(), newId.ToString());
        }

        // część właściwa programu
        [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
        private static void OptimizePaths()
        {
            var path = new List<Match>();
            var optimized = true;

            // funkcja rekurencyjnego przechodzenia po drzewie
            void StepInto(Match match)
            {
                var first = match.GetFirstExit();
                var second = match.GetSecondExit();

                // przetwarzam wykrytą ścieżkę, kiedy napotkam if'a lub koniec
                if (second != 0 || first == 0)
                {
                    MergeOptimization(path, ref optimized);
                    //AssignmentOptimization(path, ref optimized);

                    path.Clear();
                }

                // koniec bloku optymalizującego, ustalam ścieżkę i przechodzę po drzewie...
                if (second == 0 && first != 0 && match.GetId() != 0)
                {
                    path.Add(match);
                }

                foreach (var m in from m in IterateGsa() let id = m.GetId() where id != 0 && (id == first || id == second) select m)
                {
                    StepInto(m);
                }
            }

            while (!(optimized ^= true))
            {
                StepInto(IterateGsa().First()); // rozpocznij przechodzenie po gsa
            }
        }

        private static void MergeOptimization(IList<Match> path, ref bool optimized)
        {
            // w późniejszej wersji umieszczę tutaj stan

            for (var i = path.Count - 1; i > 0; --i)
            {
                var current = path[i];
                var previous = path[i - 1];
                var currName = current.GetName();
                var prevName = previous.GetName();

                var prevVars = GetOperationsByName(prevName).SelectMany(ExtractVariablesFromOperation);
                var newLeftVars = GetOperationsByName(currName).Select(operation => ExtractVariablesFromOperation(operation).First());

                if (prevVars.Intersect(newLeftVars).Any())
                {
                    // zbiory się przecinają, nic nie robię
                    continue;
                }

                optimized = true;

                RemoveInstruction(currName);
                path.RemoveAt(i);

                var oldIndex = GetTxtIndexForName(currName);
                var index = GetTxtIndexForName(prevName);
                var line = Txt[index];

                Txt[index] = line.Substring(0, line.IndexOf('=') + 1)
                           + ExtractInstructionsFromTxtLine(Txt[oldIndex])
                            .Concat(ExtractInstructionsFromTxtLine(line))
                            .OrderBy(word => word)
                            .Aggregate(string.Empty, (s, e) => $"{s} {e}");

                Txt.RemoveAt(oldIndex);
            }
        }

        private static void AssignmentOptimization(IList<Match> path, ref bool optimized)
        {
            // zmienne, które zostały już przypisane w obecnej ścieżce
            var assignedVariables = new HashSet<string>();

            // iteruję; od tyłu, żeby zachować możliwość usuwania na bieżąco zbędnych bloków
            for (var i = path.Count - 1; i >= 0; i--)
            {
                var name = path[i].GetName();
                var index = GetTxtIndexForName(name);
                var line = Txt[index];

                // multiple y's after =
                foreach (var instruction in ExtractInstructionsFromTxtLine(line))
                {
                    var instructionIndex = Txt.FindIndex(l => l.StartsWith(instruction));
                    var operation = ExtractOperationFromTxtLine(Txt[instructionIndex]);
                    var variable = ExtractVariablesFromOperation(operation).First();

                    if (assignedVariables.Add(variable))
                    {
                        continue;
                    }

                    optimized = true;

                    // optymalizacja konieczna!
                    Console.WriteLine($"USUWAM z {line} - przypisanie {instruction} {operation} jest zbędne!");

                    line = line.Replace($" {instruction}", string.Empty);

                    // brak więcej operacji, obsługuję gsa
                    if (!ExtractInstructionsFromTxtLine(line).Any())
                    {
                        path.RemoveAt(i);
                        RemoveInstruction(instruction);
                        Txt.RemoveAt(index);
                    }
                    else
                    {
                        // usuń tylko jedne przypisanie z bloczka
                        Txt[index] = line;
                    }

                    // w przypadku txt - usuń operację z txt, kiedy nie jest nigdzie więcej używana
                    if (Txt.Count(l => l.Contains(instruction)) == 1)
                    {
                        Txt.RemoveAt(instructionIndex);
                    }
                }
            }
        }

        private static void OptimizeVariables()
        {
            var i = 0;

            foreach (var match in IterateGsa())
            {
                var name = match.GetName();

                if (name[0] == 'Y')
                {
                    var newName = "Y" + ++i;

                    if (name != newName)
                    {
                        var gIndex = GetGsaIndexForMatch(match);
                        Gsa[gIndex] = Gsa[gIndex].Replace(name, newName);

                        var tIndex = GetTxtIndexForName(name);
                        Txt[tIndex] = Txt[tIndex].Replace(name, newName);
                    }
                }
            }
        }
    }
}