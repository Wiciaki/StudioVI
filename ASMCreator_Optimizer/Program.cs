namespace ASMCreator_Optimizer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
#if !DEBUG
    using System.Diagnostics;
    using System.Threading;
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
            Console.WriteLine(content.Aggregate(string.Empty, (e, s) => e + s + Environment.NewLine));
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

        // kod jest uniwersalny i zadziała na windows, linux, mac - wystarczy skompilować pod odpowiednią wersję systemu
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

                if (source == null)
                {
                    continue;
                }

                GetListByIndex(i).AddRange(File.ReadAllLines(source));
            }

            PrintFile("WCZYTANO TXT:", Txt);
            PrintFile("WCZYTANO GSA:", Gsa);

            Console.WriteLine("PRZETWARZAM...");

            OptimizePaths();
            OptimizeGsaVariables();

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
                    Console.WriteLine($"Sugerowane usunięcie z pulpitu starego folderu {dir}");
                    //Directory.Delete(dir, true);
                }

                Thread.Sleep(3000);
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

                // usunąć po dodaniu obsługi .mic
                if (Path.GetExtension(source) == ".mic")
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

        private static readonly Regex GsaRegex = new Regex(@"(\d+)[\t ]+([A-Za-z0-9]+)[\t ]+(\d+)[\t ]+(\d+)");

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

        private static int GetIndexForMatch(Match match)
        {
            var id = match.GetId().ToString();

            return Gsa.Skip(1).ToList().FindIndex(line => line.TrimStart().StartsWith(id)) + 1;
        }

        private static int GetTxtIndexForName(string name)
        {
            return Txt.FindIndex(line => line.StartsWith(name));
        }

        //// "y2" -> "x:=y+2"
        //private static string GetOperationForInstruction(string instruction)
        //{
        //    return ExtractOperationFromTxtLine(Txt.Find(l => l.Contains(instruction)));
        //}

        //// "x:=y+2" -> "y2"
        //private static string GetInstructionForOperation(string operation)
        //{
        //    return Txt[Txt.FindIndex(line => line.Contains(operation))].Substring(0, 2);
        //}

        // "Y5" -> { "x:=7", "y:=5" }
        private static IEnumerable<string> GetOperationsByName(string name)
        {
            var i = GetTxtIndexForName(name);

            return i == -1 ? Enumerable.Empty<string>() : ExtractInstructionsFromTxtLine(Txt[i]);
        }

        // "Y3 = y3 y4" -> { "y3", "y4" }
        private static IEnumerable<string> ExtractInstructionsFromTxtLine(string line)
        {
            return line.Split(' ').Skip(2);
        }

        private static IEnumerable<Match> GetChildrenForId(params int[] ids)
        {
            return from match in IterateGsa() let id = match.GetId() where id != 0 && ids.Contains(id) select match;
        }

        private static IEnumerable<Match> GetPathForMatch(Match match)
        {
            do
            {
                yield return match;
                match = GetChildrenForId(match.GetFirstExit()).FirstOrDefault();
            }
            while (match?.GetSecondExit() == 0 && GetParents(match).Count() == 1 && match.GetName().ToLower() != "end");
        }

        private static IEnumerable<string> GetOperationsForPath(IEnumerable<Match> path)
        {
            return path.Select(match => match.GetName()).SelectMany(GetOperationsByName);
        }

        private static IEnumerable<Match> GetParents(Match match)
        {
            var id = match.GetId();

            return from m in IterateGsa() where m.GetFirstExit() == id || m.GetSecondExit() == id select m;
        }

        // "y1  :  y:=1"
        private static string ExtractOperationFromTxtLine(string line)
        {
            return line.Substring(line.IndexOf(':') + 1).TrimStart();
        }

        // "y:=x1+2" -> { "y", "x1" }
        private static IEnumerable<string> ExtractVariablesFromOperation(string operation)
        {
            var array = Txt.Find(l => l.StartsWith(operation))?.Split(':') ?? throw new ArgumentException();
            array[0] = string.Empty;

            var line = string.Join(string.Empty, array).TrimStart();
            var regex = new Regex("[a-z][a-z0-9]*");

            return from match in regex.Matches(line) select match.Value;
        }

        private static void UpdatePointing(int oldId, int newId)
        {
            foreach (var match in IterateGsa().Where(match => match.GetFirstExit() == oldId || match.GetSecondExit() == oldId))
            {
                var index = GetIndexForMatch(match);
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
            UpdatePointing(oldId, newId);

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

                    UpdatePointing(oldId, newId);
                }
            }

            Gsa[0] = Gsa[0].Replace(oldId.ToString(), newId.ToString());
        }

        private static void OptimizeGsaVariables()
        {
            var i = 0;

            foreach (var match in IterateGsa())
            {
                var name = match.GetName();

                if (name[0] != 'Y')
                {
                    continue;
                }

                var expected = "Y" + ++i;
                var spaces = Gsa[1].Split('0')[0];

                var gIndex = GetIndexForMatch(match);
                Gsa[gIndex] = spaces + Gsa[gIndex].TrimStart().Replace(name, expected);

                var tIndex = GetTxtIndexForName(name);
                Txt[tIndex] = Txt[tIndex].Replace(name, expected);
            }
        }

        // część właściwa programu
        [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
        private static void OptimizePaths()
        {
            var path = new List<Match>();

            // funkcja rekurencyjnego przechodzenia po drzewie
            bool StepInto(Match match)
            {
                var first = match.GetFirstExit();
                var second = match.GetSecondExit();

                // przetwarzam wykrytą ścieżkę, kiedy napotkam if'a, koniec lub istnieją inne wejścia
                if (second != 0 || first == 0 || GetParents(match).Count() != 1)
                {
                    if (MergeBlocksOptimization(path))
                    {
                        return true;
                    }

                    //AssignmentOptimization(path, ref optimized);

                    path.Clear();
                }

                // koniec bloku optymalizującego, ustalam ścieżkę i przechodzę po drzewie...
                if (second == 0 && first != 0 && match.GetId() != 0)
                {
                    path.Add(match);
                }

                var children = GetChildrenForId(first, second).ToArray();

                if (children.Length == 2)
                {
                    var path1 = GetPathForMatch(children[0]).ToArray();
                    var path2 = GetPathForMatch(children[1]).ToArray();

                    if (RemovePathOptimization(path1, path2) || ExtractMutualOptimization(path1, path2))
                    {
                        return true;
                    }
                }

                return children.Any(StepInto);
            }

            // rekurencyjne przechodzenie po gsa
            while (StepInto(IterateGsa().First()))
            { }
        }

        private static bool MergeBlocksOptimization(IList<Match> path)
        {
            var optimized = false;

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

            return optimized;
        }

        private static bool RemovePathOptimization(IList<Match> path1, IList<Match> path2)
        {
            // działa tylko wtedy, gdy mamy blok warunkowy, z którego wychodzą puste ścieżki po obu stronach
            // w zadanych przykładach zbędne; może się przydać w przyszłości
            return false;

            //var set1 = GetOperationsForPath(path1).ToArray();
            //var set2 = GetOperationsForPath(path2).ToArray();

            //if (!set2.Except(set1).Any())
            //{
            //    path1 = path2;
            //}
            //else if (set1.Except(set2).Any())
            //{
            //    return false;
            //}

            //var parentName = GetParents(path1[0]).Single().GetName();

            //foreach (var name in path1.Select(match => match.GetName()))
            //{
            //    RemoveInstruction(name);
            //    Txt.RemoveAt(Txt.FindIndex(line => line.StartsWith(name)));
            //}

            //RemoveInstruction(parentName);
            //Txt.RemoveAt(Txt.FindIndex(line => line.StartsWith(parentName)));

            //return true;
        }

        private static bool ExtractMutualOptimization(IList<Match> path1, IList<Match> path2)
        {
            var intersection = GetOperationsForPath(path1).Intersect(GetOperationsForPath(path2)).ToArray();

            if (intersection.Length == 0)
            {
                // nic do wyprowadzenia
                return false;
            }

            // oczywiście, nie musi być tak że pierwszy element w ścieżce ma tylko 1 rodzica
            var child = GetParents(path1[0]).Single(); // wskazuje na blok warunkowy
            // var parent = GetParents(child).Single();

            // dla nadania nowemu bloczkowi id oraz nazwy
            var last = IterateGsa().Reverse().First(match => match.GetName().StartsWith("Y"));

            var gsaIndex = GetIndexForMatch(last);
            var txtIndex = Txt.IndexOf(string.Empty);

            var id = last.GetId();
            var childId = child.GetId();

            // tworzę miejsce na nowe bloki
            foreach (var match in IterateGsa().Reverse())
            {
                var oldId = match.GetId();
                var newId = oldId + intersection.Length;

                UpdatePointing(oldId, newId);

                var i = GetIndexForMatch(match);
                Gsa[i] = Gsa[i].Replace(oldId.ToString(), newId.ToString());

                if (oldId == childId)
                {
                    break;
                }
            }

            // dodaję nowe operacje
            for (var i = 1; i <= intersection.Length; ++i)
            {
                var bindId = childId + intersection.Length;
                UpdatePointing(bindId, id + i);

                var name = $"Y{id + i}";
                Gsa.Insert(gsaIndex + i, $"  {id + i} {name}     {bindId}      0     ");
                Txt.Insert(txtIndex + i - 1, $"{name} = {intersection[i - 1]}");
            }

            var names = new[] { path1, path2 }.SelectMany(p => p).Select(m => m.GetName()).Distinct();

            // usun wszystkie zbedne operacje z Txt
            foreach (var name in names)
            {
                var index = GetTxtIndexForName(name);
                var line = intersection.Aggregate(Txt[index], (s, e) => s.Replace(" " + e, string.Empty));

                if (line.TrimEnd().EndsWith('='))
                {
                    RemoveInstruction(name);
                    Txt.RemoveAt(index);
                }
                else
                {
                    Txt[index] = line;
                }
            }

            return true;

            //var childId = 0;

            //Gsa.Insert(insertIndex, );

            ////

            //var parent = GetParents(helper).Single();
            //var parentName = parent.GetName();
            //var grandparents = GetParents(parent).ToArray();

            //foreach (var match in IterateGsa().Reverse())
            //{
            //    var id = match.GetId();
            //    var i = GetIndexForMatch(match);

            //    Gsa[i] = Gsa[i].Replace(id.ToString(), (id + 1).ToString());
            //    UpdatePointing(id, id + 1);

            //    if (id == insertId)
            //    {
            //        break;
            //    }
            //}

            //var newId = int.Parse(IterateGsa().Reverse().First(m => m.GetName().StartsWith("Y")).GetName().Substring(1)) - 1;
            //var value = Gsa[insertIndex].Replace(oldId.ToString(), newId.ToString()).Replace(helper.GetFirstExit().ToString(), Gsa[Gsa.FindIndex(line => line.Contains(parentName))].TrimStart().Substring(0, 1));

            //Gsa.Insert(insertIndex, value);

            //foreach (var match in grandparents)
            //{
            //    var index = GetIndexForMatch(match);
            //    var m = GsaMatch(Gsa[index]);

            //    Gsa[index] = Gsa[index].Replace(m.GetFirstExit().ToString(), newId.ToString());
            //}

            //var instructions = mutual.Select(GetInstructionForOperation).ToArray();
            //Txt.Insert(Txt.IndexOf(string.Empty), $"Y{newId + 1} = {string.Join(' ', instructions.OrderBy(o => o))}");

            //// usun wszystkie zbedne operacje z Txt
            //foreach (var name in new[] { path1, path2 }.SelectMany(p => p).Select(m => m.GetName()))
            //{
            //    var index = GetTxtIndexForName(name);
            //    var line = instructions.Aggregate(Txt[index], (s, e) => s.Replace(e, string.Empty));

            //    if (line.TrimEnd().EndsWith('='))
            //    {
            //        Txt.RemoveAt(index);
            //        RemoveInstruction(name);
            //    }
            //    else
            //    {
            //        Txt[index] = line;
            //    }
            //}
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
    }
}