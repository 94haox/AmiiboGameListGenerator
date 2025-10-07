﻿using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Serialization;
using AmiiboGameList.ConsoleClasses;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace AmiiboGameList;

public class Program
{
    /// <summary>
    /// A shared HttpClient instance to be used throughout the program.
    /// </summary>
    public static HttpClient client = new();

    /// <summary>
    /// The lazy instance of the AmiiboDataBase
    /// </summary>
    private static readonly Lazy<DBRootobjectInstance> lazy = new(() => new DBRootobjectInstance());

    /// <summary>
    /// Gets the instance of the AmiiboDataBase.
    /// </summary>
    /// <value>
    /// The instance of the AmiiboDataBase.
    /// </value>
    public static DBRootobjectInstance BRootobject => lazy.Value;
    private static string inputPath;
    private static string outputPath = @"games_info.json";
    private static int parallelism = 4;
    private static readonly Dictionary<Hex, Games> export = new();

    public static async Task<string> GetAmiilifeStringAsync(string url, int attempts = 5)
    {
        var handleError = new Func<int, string, Task<bool>>(async (attempt, message) =>
        {
            Debugger.Log(message, Debugger.DebugLevel.Error);

            if (attempt >= (attempts - 1))
            {
                return true;
            }

            var delay = (attempt + 1) * 5000;
            Debugger.Log($"Retrying in {delay / 1000} seconds", Debugger.DebugLevel.Verbose);
            await Task.Delay(delay);

            return false;
        });

        // Attempt to load the html up to 5 times when encountering a WebException
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                using var response = await client.GetAsync(url);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new HttpRequestException($"404 Not Found: {url}", null, response.StatusCode);
                }
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (WebException ex)
            {
                if (handleError(i, $"({i + 1}/{attempts}) Error while loading {url}\n{ex.Message}").Result)
                {
                    throw;
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                if (handleError(i, $"({i + 1}/{attempts}) Timeout error while loading {url}\n{ex.Message}").Result)
                {
                    throw;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode.HasValue && (int)ex.StatusCode > 499 && (int)ex.StatusCode < 600)
            {
                if (handleError(i, $"({i + 1}/{attempts}) HTTP {(int)ex.StatusCode} error while loading {url}\n{ex.Message}").Result)
                {
                    throw;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // 404 错误直接抛出，不重试
                throw new HttpRequestException($"404 Not Found: {url}", ex, HttpStatusCode.NotFound);
            }
            catch (Exception)
            {
                throw;
            }
        }

        throw new Exception("Error occurred in Program.GetAmiilifeStringAsync.  This should never be reached.");
    }

    /// <summary>
    /// Mains this instance.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="XmlSerializer">typeof(Switchreleases)</exception>
    public static int Main(string[] args)
    {
        ParseArguments(args);

        // Load Regex for removing copyrights, trademarks, etc.
        Regex rx = new(@"[®™]", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Load amiibo data
        Debugger.Log("Loading amiibo");
        try
        {
            string amiiboJSON = default;
            if (string.IsNullOrEmpty(inputPath))
            {
                Debugger.Log("Downloading amiibo database", Debugger.DebugLevel.Verbose);
                try
                {
                    amiiboJSON = Program.client.GetStringAsync("https://raw.githubusercontent.com/N3evin/AmiiboAPI/master/database/amiibo.json").Result;
                }
                catch (Exception e)
                {
                    Debugger.Log("Error while downloading amiibo.json, please check internet:\n" + e.Message, Debugger.DebugLevel.Error);
                    Environment.Exit((int)Debugger.ReturnType.InternetError);
                }
            }
            else
            {
                amiiboJSON = File.ReadAllText(inputPath);
            }

            Debugger.Log("Processing amiibo database", Debugger.DebugLevel.Verbose);
            BRootobject.rootobject = JsonConvert.DeserializeObject<DBRootobject>(amiiboJSON);

            foreach (KeyValuePair<Hex, DBAmiibo> entry in BRootobject.rootobject.amiibos)
            {
                entry.Value.ID = entry.Key;
            }
        }
        catch (Exception ex)
        {
            Debugger.Log("Error loading amiibo.json:\n" + ex.Message, Debugger.DebugLevel.Error);
            Environment.Exit((int)Debugger.ReturnType.DatabaseLoadingError);
        }

        // Load Wii U games
        Debugger.Log("Loading Wii U games");
        Debugger.Log("Processing Wii U database", Debugger.DebugLevel.Verbose);
        try
        {
            Games.WiiUGames = JsonConvert.DeserializeObject<List<GameInfo>>(Properties.Resources.WiiU);
        }
        catch (Exception ex)
        {
            Debugger.Log("Error loading Wii U games:\n" + ex.Message, Debugger.DebugLevel.Error);
            Environment.Exit((int)Debugger.ReturnType.DatabaseLoadingError);
        }

        // Load 3DS games
        Debugger.Log("Loading 3DS games");
        try
        {
            byte[] DSDatabase = default;
            try
            {
                Debugger.Log("Downloading 3DS database", Debugger.DebugLevel.Verbose);
                DSDatabase = Program.client.GetByteArrayAsync("http://3dsdb.com/xml.php").Result;
            }
            catch (Exception ex)
            {
                Debugger.Log("Error while downloading 3DS database, please check internet:\n" + ex.Message, Debugger.DebugLevel.Error);
                Environment.Exit((int)Debugger.ReturnType.InternetError);
            }

            Debugger.Log("Processing 3DS database", Debugger.DebugLevel.Verbose);
            XmlSerializer serializer = new(typeof(DSreleases));
            using MemoryStream stream = new(DSDatabase);
            Games.DSGames = ((DSreleases)serializer.Deserialize(stream)).release.ToList();
        }
        catch (Exception ex)
        {
            Debugger.Log("Error loading 3DS games:\n" + ex.Message, Debugger.DebugLevel.Error);
            Environment.Exit((int)Debugger.ReturnType.DatabaseLoadingError);
        }

        // Load Switch games
        Debugger.Log("Loading Switch/switch2 games");
        try
        {
            string BlawarDatabase = default;
            // Try loading the database
            Debugger.Log("Downloading Switch database", Debugger.DebugLevel.Verbose);
            try
            {
                BlawarDatabase = Program.client.GetStringAsync("https://raw.githubusercontent.com/blawar/titledb/master/US.en.json").Result;
            }
            catch (Exception ex)
            {
                Debugger.Log("Error while downloading switch database, please check internet:\n" + ex.Message, Debugger.DebugLevel.Error);
                Environment.Exit((int)Debugger.ReturnType.InternetError);
            }

            Debugger.Log("Processing Switch database", Debugger.DebugLevel.Verbose);
            // Parse the loaded JSON
            Games.SwitchGames = (Lookup<string, string>)JsonConvert.DeserializeObject<Dictionary<Hex, SwitchGame>>(BlawarDatabase)
                // Make KeyValuePairs to turn into a Lookup and decode the HTML encoded name
                .Select(x => new KeyValuePair<string, string>(HttpUtility.HtmlDecode(x.Value.name), x.Value.id)).Where(y => y.Value != null)
                // Convert to Lookup for faster searching while allowing multiple values per key and apply regex
                .ToLookup(x => rx.Replace(x.Key, "").Replace('’', '\'').ToLower(), x => x.Value);
            Games.Switch2Games = (Lookup<string, string>)JsonConvert.DeserializeObject<Dictionary<Hex, Switch2Game>>(BlawarDatabase)
                // Make KeyValuePairs to turn into a Lookup and decode the HTML encoded name
                .Select(x => new KeyValuePair<string, string>(HttpUtility.HtmlDecode(x.Value.name), x.Value.id)).Where(y => y.Value != null)
                // Convert to Lookup for faster searching while allowing multiple values per key and apply regex
                .ToLookup(x => rx.Replace(x.Key, "").Replace('’', '\'').ToLower(), x => x.Value);
        }
        catch (Exception ex)
        {
            Debugger.Log("Error loading Switch games:\n" + ex.Message, Debugger.DebugLevel.Error);
            Environment.Exit((int)Debugger.ReturnType.DatabaseLoadingError);
        }

        Debugger.Log("Done loading!");

        // List to keep track of missing games
        Games.missingGames = new();

        // Counter to keep track of how many amiibo we've done
        int AmiiboCounter = 0;
        int TotalAmiibo = BRootobject.rootobject.amiibos.Count;

        Debugger.Log("Processing amiibo");
        // Iterate over all amiibo and get game info
        _ = Parallel.ForEach(BRootobject.rootobject.amiibos, new ParallelOptions()
        {
            MaxDegreeOfParallelism = parallelism
        }, (DBamiibo) =>
        {
            Games exportAmiibo = default;
            try
            {
                exportAmiibo = ParseAmiibo(DBamiibo.Value);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // 404 错误已经被 ParseAmiibo 处理，这里不需要额外处理
                Debugger.Log($"404 Not Found: Skipping {DBamiibo.Value.Name} ({DBamiibo.Value.OriginalName}) - URL: {DBamiibo.Value.URL}", Debugger.DebugLevel.Warn);
                exportAmiibo = new Games(); // 返回空的 Games 对象
            }
            catch (WebException ex)
            {
                Debugger.Log($"Internet error when processing {DBamiibo.Value.Name} ({DBamiibo.Value.OriginalName})\n{ex.Message}\n{DBamiibo.Value.URL}", Debugger.DebugLevel.Error);
                Environment.Exit((int)Debugger.ReturnType.InternetError);
            }
            catch (Exception ex)
            {
                Debugger.Log($"Unexpected error when processing {DBamiibo.Value.Name} ({DBamiibo.Value.OriginalName})\n{ex.Message}", Debugger.DebugLevel.Error);
                Environment.Exit((int)Debugger.ReturnType.UnknownError);
            }

            lock (export)
            {
                export.Add(DBamiibo.Key, exportAmiibo);
            }

            // Show which amiibo just got added
            AmiiboCounter++;
            Debugger.Log($"{AmiiboCounter:D3}/{TotalAmiibo} Done with {DBamiibo.Value.OriginalName} ({DBamiibo.Value.amiiboSeries})", Debugger.DebugLevel.Verbose);
        });

        // Sort export object
        var SortedAmiibos = new AmiiboKeyValue
        {
            amiibos = export.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };

        // Write the SortedAmiibos to file as an tab-indented json
        File.WriteAllText(outputPath, JsonConvert.SerializeObject(SortedAmiibos, Formatting.Indented).Replace("  ", "\t"));

        // Inform we're done
        Debugger.Log("\nDone generating the JSON!");

        // Show missing games
        if (Games.missingGames.Count != 0)
        {
            Debugger.Log("However, the following games couldn't find their titleids and thus couldn't be added:", Debugger.DebugLevel.Warn);
            foreach (string Game in Games.missingGames.Distinct())
            {
                Debugger.Log("\t" + Game, Debugger.DebugLevel.Warn);
            }

            return (int)Debugger.ReturnType.SuccessWithErrors;
        }
        else
        {
            return 0;
        }
    }

    private static Games ParseAmiibo(DBAmiibo DBamiibo)
    {
        Games ExAmiibo = new();

        HtmlDocument htmlDoc = new();
        try
        {
            htmlDoc.LoadHtml(
                WebUtility.HtmlDecode(
                    Program.GetAmiilifeStringAsync(DBamiibo.URL).Result
                )
            );
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // 如果是 404 错误，记录日志并返回空的 Games 对象
            Debugger.Log($"404 Not Found: Skipping {DBamiibo.Name} ({DBamiibo.OriginalName}) - URL: {DBamiibo.URL}", Debugger.DebugLevel.Warn);
            return ExAmiibo;
        }

        // Get the games panel
        HtmlNodeCollection GamesPanel = htmlDoc.DocumentNode.SelectNodes("//*[@class='games panel']/a");
        if (GamesPanel.Count == 0)
        {
            Debugger.Log("No games found for " + DBamiibo.Name, Debugger.DebugLevel.Verbose);
        }

        // Iterate over each game in the games panel
        foreach (HtmlNode node in GamesPanel)
        {
            // Get the name of the game
            Game game = new()
            {
                gameName = node.SelectSingleNode(".//*[@class='name']/text()[normalize-space()]").InnerText.Trim().Replace("Poochy & ", "").Trim().Replace("Ace Combat Assault Horizon Legacy +", "Ace Combat Assault Horizon Legacy+").Replace("Power Pros", "Jikkyou Powerful Pro Baseball"),
                gameID = new(),
                amiiboUsage = new()
            };

            // Get the amiibo usages
            foreach (HtmlNode amiiboUsage in node.SelectNodes(".//*[@class='features']/li"))
            {
                game.amiiboUsage.Add(new()
                {
                    Usage = amiiboUsage.GetDirectInnerText().Trim(),
                    write = amiiboUsage.SelectSingleNode("em")?.InnerText == "(Read+Write)"
                });
            }

            // Sort amiiboUsage alphabetically by Usage
            game.amiiboUsage.Sort((x, y) => string.Compare(x.Usage, y.Usage, StringComparison.OrdinalIgnoreCase));

            if (DBamiibo.Name == "Shadow Mewtwo")
            {
                game.gameName = "Pokkén Tournament";
            }

            // Add game to the correct console and get correct titleid
            Regex rgx = new("[^a-zA-Z0-9 -]");
            switch (node.SelectSingleNode(".//*[@class='name']/span").InnerText.Trim().ToLower())
            {
                case "switch":
                    try
                    {
                        game.gameID = Games.SwitchGames[game.sanatizedGameName.ToLower()].ToList();

                        if (game.gameID.Count == 0)
                        {
                            game.gameID = game.sanatizedGameName switch
                            {
                                "Cyber Shadow" => new() { "0100C1F0141AA000" },
                                "Jikkyou Powerful Pro Baseball" => new() { "0100E9C00BF28000" },
                                "Shovel Knight Pocket Dungeon" => new() { "01006B00126EC000" },
                                "Shovel Knight Showdown" => new() { "0100B380022AE000" },
                                "Super Kirby Clash" => new() { "01003FB00C5A8000" },
                                "The Legend of Zelda: Echoes of Wisdom" => new() { "01008CF01BAAC000" },
                                "The Legend of Zelda: Skyward Sword HD" => new() { "01002DA013484000" },
                                "Yu-Gi-Oh! Rush Duel Saikyo Battle Royale" => new() { "01003C101454A000" },
                                _ => throw new Exception()
                            };
                        }

                        game.gameID = game.gameID.Order().Distinct().ToList();
                        lock (ExAmiibo.gamesSwitch)
                        {
                            ExAmiibo.gamesSwitch.Add(game);
                        }
                    }
                    catch
                    {
                        lock (Games.missingGames)
                        {
                            Games.missingGames.Add(game.gameName + " (Switch)");
                        }
                    }

                    break;
                case "switch 2":
                    try
                    {
                        game.gameID = Games.Switch2Games[game.sanatizedGameName.ToLower()].ToList();

                        if (game.gameID.Count == 0)
                        {
                            game.gameID = game.sanatizedGameName switch
                            {
                                // 这里可以添加Switch2特定的游戏ID映射
                                 "Donkey Kong Bananza" => new() { "70010000096809" },
                                _ => throw new Exception()
                            };
                        }

                        game.gameID = game.gameID.Order().Distinct().ToList();
                        lock (ExAmiibo.gamesSwitch2)
                        {
                            ExAmiibo.gamesSwitch2.Add(game);
                        }
                    }
                    catch
                    {
                        lock (Games.missingGames)
                        {
                            Games.missingGames.Add(game.gameName + " (Switch2)");
                        }
                    }

                    break;
                case "wii u":
                    try
                    {
                        string[] gameIDs = Games.WiiUGames.Find(WiiUGame => WiiUGame.Name.Contains(game.gameName, StringComparer.OrdinalIgnoreCase))?.Ids;
                        if (gameIDs?.Length == 0 || gameIDs == null)
                        {
                            game.gameID = game.gameName switch
                            {
                                "Shovel Knight Showdown" => new() { "000500001016E100", "0005000010178F00", "0005000E1016E100", "0005000E10178F00", "0005000E101D9300" },
                                _ => throw new Exception()
                            };
                        }
                        else
                        {
                            foreach (string ID in gameIDs)
                            {
                                game.gameID.Add(ID[..16]);
                            }
                        }

                        game.gameID = game.gameID.Order().Distinct().ToList();

                        lock (ExAmiibo.gamesWiiU)
                        {
                            ExAmiibo.gamesWiiU.Add(game);
                        }
                    }
                    catch
                    {
                        lock (Games.missingGames)
                        {
                            Games.missingGames.Add(game.gameName + " (Wii U)");
                        }
                    }

                    break;
                case "3ds":
                    try
                    {
                        List<DSreleasesRelease> games = Games.DSGames.FindAll(DSGame => rgx.Replace(WebUtility.HtmlDecode(DSGame.name).ToLower(), "").Contains(rgx.Replace(game.gameName.ToLower(), "")));
                        if (games.Count == 0)
                        {
                            game.gameID = game.gameName switch
                            {
                                "Style Savvy: Styling Star" => new() { "00040000001C2500" },
                                "Metroid Prime: Blast Ball" => new() { "0004000000175300" },
                                "Mini Mario & Friends amiibo Challenge" => new() { "000400000016C300", "000400000016C200" },
                                "Team Kirby Clash Deluxe" => new() { "00040000001AB900", "00040000001AB800" },
                                "Kirby's Extra Epic Yarn" => new() { "00040000001D1F00" },
                                "Kirby's Blowout Blast" => new() { "0004000000196F00" },
                                "BYE-BYE BOXBOY!" => new() { "00040000001B5400", "00040000001B5300" },
                                "Azure Striker Gunvolt 2" => new() { "00040000001A6E00" },
                                "niconico app" => new() { "0005000010116400" },
                                _ => throw new Exception(),
                            };
                        }

                        games.ForEach(DSGame =>
                            game.gameID.Add(DSGame.titleid[..16]));

                        game.gameID = game.gameID.Order().Distinct().ToList();

                        lock (ExAmiibo.games3DS)
                        {
                            ExAmiibo.games3DS.Add(game);
                        }
                    }
                    catch
                    {
                        lock (Games.missingGames)
                        {
                            Games.missingGames.Add(game.gameName + " (3DS)");
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        // Sort all gamelists
        ExAmiibo.gamesSwitch.Sort((x, y) => string.Compare(x.gameName, y.gameName, StringComparison.OrdinalIgnoreCase));
        ExAmiibo.gamesSwitch2.Sort((x, y) => string.Compare(x.gameName, y.gameName, StringComparison.OrdinalIgnoreCase));
        ExAmiibo.gamesWiiU.Sort((x, y) => string.Compare(x.gameName, y.gameName, StringComparison.OrdinalIgnoreCase));
        ExAmiibo.games3DS.Sort((x, y) => string.Compare(x.gameName, y.gameName, StringComparison.OrdinalIgnoreCase));

        // Return the created amiibo
        return ExAmiibo;
    }

    private static void ParseArguments(string[] args)
    {
        if (args.Length != 0)
        {
            Debugger.Log($"Running with these arguments: {string.Join(' ', args)}");

            // Show help message
            if (args.Contains("-h") || args.Contains("-help"))
            {
                StringBuilder sB = new();
                _ = sB.AppendLine("Usage:");
                _ = sB.AppendLine("-i | -input {filepath} to specify input json location");
                _ = sB.AppendLine("-o | -output {filepath} to specify output json location");
                _ = sB.AppendLine("-p | -parallelism {value} to specify the max degree of parallelism");
                _ = sB.AppendLine("-l | -log {value} to set the logging level, can pick from verbose, info, warn, error or from 0 to 3 respectively");
                _ = sB.AppendLine("-h | -help to show this message");
                Debugger.Log(sB.ToString());
                Environment.Exit(0);
            }
        }

        Debugger.CurrentDebugLevel = Debugger.DebugLevel.Info;

        // Loop through arguments
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-i":
                case "-input":
                    if (File.Exists(args[i + 1]) || args.Contains("-i"))
                    {
                        inputPath = args[i + 1];
                        i++;
                        continue;
                    }
                    else
                    {
                        throw new FileNotFoundException($"Input file '{args[i + 1]}' not found");
                    }
                case "-o":
                case "-output":
                    if (Directory.Exists(Path.GetDirectoryName(args[i + 1])))
                    {
                        outputPath = args[i + 1];
                        i++;
                        continue;
                    }
                    else
                    {
                        throw new DirectoryNotFoundException($"Input directory '{args[i + 1]}' not found");
                    }
                case "-p":
                case "-parallelism":
                    parallelism = int.Parse(args[i + 1]);
                    continue;

                case "-l":
                case "-log":
                    if (Enum.TryParse(args[i + 1], true, out Debugger.DebugLevel debugLevel) && Enum.IsDefined(typeof(Debugger.DebugLevel), debugLevel))
                    {
                        Debugger.CurrentDebugLevel = debugLevel;
                        Debugger.Log($"Setting DebugLevel to {Enum.GetName(typeof(Debugger.DebugLevel), debugLevel)}", Debugger.DebugLevel.Verbose);
                        i++;
                        continue;
                    }
                    else
                    {
                        throw new ArgumentException($"Incorrect debug level passed: {args[i + 1]}");
                    }
                default:
                    break;
            }
        }

        Debugger.Log("Done parsing arguments", Debugger.DebugLevel.Verbose);
        Debugger.Log(default);
    }
}
