namespace ASMCreator_Optimizer
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

    internal static class Program
    {
        private static readonly List<string> Gsa = new List<string>(), Txt = new List<string>(), Mic = new List<string>();

        private const string DesktopFolderName = "Studio";

        private static void Print(IEnumerable<string> content)
        {
            Console.WriteLine();

            // zapisz i zmień kolor
            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;

            Console.WriteLine(string.Join(Environment.NewLine, content));

            // przywróć
            Console.ForegroundColor = color;
            Console.WriteLine();
        }

        private static void EndExecution(string message)
        {
            Console.WriteLine(message);
            Console.WriteLine("Zamknij program i spróbuj jeszcze raz");
            Console.ReadKey(true);
        }

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
                args = Directory.GetFiles(Path.Combine(desktop, DesktopFolderName));
            }
#endif

            // czytaj pliki o tym rozszerzeniu z drag & drop
            // działa też z wejściem jako parametr
            var sources = new[] { "gsa", "txt", "mic" }.Select(ext => args.SingleOrDefault(p => Path.GetExtension(p) == $".{ext}")).ToArray();

            if (sources.All(s => s == null))
            {
                EndExecution("Przeciągnij pliki do optymalizacji na .exe lub podaj argumenty");
                return;
            }

            if (!sources.Any(s => s.EndsWith("gsa")))
            {
                EndExecution("Nie podano wymaganego pliku gsa");
                return;
            }

            if (!sources.Any(s => s.EndsWith("txt")))
            {
                EndExecution("Nie podano wymaganego pliku txt");
                return;
            }

            var content = Array.ConvertAll(sources, f => f != null ? File.ReadAllLines(f) : null);
            var lists = new[] { Gsa, Txt, Mic };

            for (var i = 0; i < sources.Length; ++i)
            {
                lists[i].AddRange(content[i]);
            }

            Console.WriteLine("WCZYTANO TXT:");
            Print(Txt);

            Console.WriteLine("WCZYTANO GSA:");
            Print(Gsa);

            Optimize();

            Console.WriteLine();
            Console.WriteLine("OPTYMALIZACJA ZAKOŃCZONA");
            Console.WriteLine();

            Console.WriteLine("GSA - PO EDYCJI:");
            Print(Gsa);

            Console.WriteLine("TXT - PO EDYCJI:");
            Print(Txt);
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
                File.WriteAllLines(Path.Combine(directory, fileName), lists[i]);

                Console.WriteLine($"Zapisano {directoryName}/{fileName}");
            }

            Process.Start(new ProcessStartInfo
                          {
                              FileName = directory,
                              UseShellExecute = true,
                              Verb = "open"
                          });

            Console.WriteLine("Otwieram folder z wynikiem");
#endif
        }

        private static int GetId(this Match match)
        {
            return int.Parse(match.Groups["1"].Value);
        }

        private static string GetName(this Match match)
        {
            return match.Groups["2"].Value;
        }

        private static int GetFirstNode(this Match match)
        {
            return int.Parse(match.Groups["3"].Value);
        }

        private static int GetSecondNode(this Match match)
        {
            return int.Parse(match.Groups["4"].Value);
        }

        private static Match GsaMatch(string line)
        {
            var gsaLineFormat = new Regex(@"(\d+) ([A-Za-z0-9]+) +(\d+) +(\d+)");
            var match = gsaLineFormat.Match(line);

            if (!match.Success)
            {
                throw new ArgumentException("Uszkodzony plik gsa");
            }

            return match;
        }

        private static readonly Regex GsaLineFormat = new Regex(@"(\d+) ([A-Za-z0-9]+) +(\d+) +(\d+)");

        private static IEnumerable<Match> IterateGsaMatches()
        {
            for (var i = 1; i <= Gsa.Count - 1; ++i)
            {
                var match = GsaLineFormat.Match(Gsa[i]);

                if (!match.Success)
                {
                    throw new ArgumentException("Uszkodzony plik gsa");
                }

                yield return match;
            }
        }

        private static int GetGsaIndexForMatch(Match m)
        {
            var id = m.GetId().ToString();

            return Gsa.FindIndex(l => l.TrimStart().StartsWith(id));
        }

        // część właściwa programu
        [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
        [SuppressMessage("ReSharper", "CognitiveComplexity")]
        private static void Optimize()
        {
            var path = new List<Match>();
            StepInto(IterateGsaMatches().First()); // rozpocznij przechodzenie po gsa

            void StepInto(Match m)
            {
                var firstNode = m.GetFirstNode();
                var secondNode = m.GetSecondNode();

                // optymalizuję i kasuję ścieżkę, kiedy napotkam if'a lub koniec
                if (secondNode != 0 || firstNode == 0)
                {
                    // symbole, które zostały już przypisane w obecnej ścieżce
                    var assignedSymbols = new List<string>();

                    // iteruję od tyłu, żeby zachować możliwość usuwania na bieżąco zbędnych bloków
                    for (var i = path.Count - 1; i >= 0; i--)
                    {
                        var stepName = path[i].GetName(); // ex. "Y1"
                        var line1Index = Txt.FindIndex(l => l.StartsWith(stepName));
                        var line1 = Txt[line1Index]; // ex. "Y4 = y4 y5"

                        // ex. { "y4", "y5" }
                        var operations = line1!.Split(' ').Skip(2).ToArray();

                        // multiple y's after =
                        foreach (var operation in operations)
                        {
                            var line2Index = Txt.FindIndex(l => l.StartsWith(operation));
                            var line2 = Txt[line2Index];
                            var calculation = line2!.Substring(line2.IndexOf(':') + 1).Trim(); // ex. "x:=3"

                            // pobierz nazwę zmiennej z wyrażenia
                            var symbol = calculation.Substring(0, calculation.IndexOf(':')); // ex. "x"

                            if (!assignedSymbols.Contains(symbol))
                            {
                                assignedSymbols.Add(symbol);
                                continue;
                            }

                            // optymalizacja konieczna!
                            Console.WriteLine($"USUWAM z {line1} - przypisanie {calculation} o id {operation} jest zbędne!");

                            var newline = line1.Replace(" " + operation, "");

                            // brak więcej operacji
                            if (newline.Replace('=', ' ').TrimEnd() == stepName)
                            {
                                // obsługa gsa
                                // usuwam bloczek
                                var relinkedIndex = Gsa.FindIndex(l => l.Contains(stepName));

                                var oldMatch = GsaMatch(Gsa[relinkedIndex]);
                                var oldId = oldMatch.GetId();
                                var newId = oldMatch.GetFirstNode();

                                // mogę tak zrobić, bo iteruję po path od tyłu
                                path.RemoveAt(i);
                                Gsa.RemoveAt(relinkedIndex);

                                // node update
                                foreach (var match in IterateGsaMatches().Where(match => match.GetFirstNode() == oldId || match.GetSecondNode() == oldId))
                                {
                                    var index = GetGsaIndexForMatch(match);
                                    var l = Gsa[index];

                                    //Console.WriteLine($"ZAMIANA Z ID {oldId} NA {newId} W LINII");
                                    //Console.WriteLine(l);

                                    var sName = match.GetName();
                                    var length = l.IndexOf(sName, StringComparison.InvariantCulture) + sName.Length;

                                    Gsa[index] = l.Substring(0, length) + l.Substring(length).Replace(oldId.ToString(), newId.ToString());
                                }

                                Txt.RemoveAt(line1Index);

                            }
                            else
                            {
                                // usuń tylko jedne przypisanie z bloczka
                                Txt[line1Index] = newline;
                            }

                            // w przypadku txt - usuń operację z txt, kiedy nie jest nigdzie więcej używana
                            if (Txt.Count(l => l.Contains(operation)) == 1)
                            {
                                // usuń zbędną operację
                                Txt.RemoveAt(line2Index);
                            }
                        }
                    }

                    path.Clear();
                }

                // koniec bloku optymalizującego, ustalam ścieżkę i przechodzę po drzewie...
                if (secondNode == 0 && firstNode != 0 && m.GetId() != 0)
                {
                    path.Add(m);
                }

                foreach (var match in from match in IterateGsaMatches() let id = match.GetId() where id != 0 && (id == firstNode || id == secondNode) select match)
                {
                    StepInto(match);
                }
            }
        }
    }
}