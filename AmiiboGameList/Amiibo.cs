using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace AmiiboGameList;

/// <summary>Class to be JSONified and exported.</summary>
public class AmiiboKeyValue
{
    public Dictionary<Hex, Games> amiibos = new();
}

public class DBRootobjectInstance
{
    public DBRootobject rootobject;
}

/// <summary>Class to map all the database data to.</summary>
public class DBRootobject
{
    public Dictionary<string, string> amiibo_series = new();
    public Dictionary<Hex, DBAmiibo> amiibos = new();
    public Dictionary<string, string> characters = new();
    public Dictionary<string, string> game_series = new();
    public Dictionary<string, string> types = new();
}

/// <summary>Amiibo class for amiibo from the database.</summary>
public class DBAmiibo
{
    public string OriginalName;
    public Hex ID;
    private readonly Lazy<string> name;
    private readonly Lazy<string> url;

    public DBAmiibo()
    {
        name = new Lazy<string>(() =>
        {
            string ReturnName = OriginalName switch
            {
                "8-Bit Link" => "Link The Legend of Zelda",
                "8-Bit Mario Classic Color" => "Mario Classic Colors",
                "8-Bit Mario Modern Color" => "Mario Modern Colors",
                "Midna & Wolf Link" => "Wolf Link",
                "Toon Zelda - The Wind Waker" => "Zelda The Wind Waker",
                "Rosalina & Luma" => "Rosalina",
                "Zelda & Loftwing" => "Zelda & Loftwing - Skyward Sword",
                "Samus (Metroid Dread)" => "Samus",
                "E.M.M.I." => "E M M I",
                "Tatsuhisa “Luke” Kamijō" => "Tatsuhisa Luke kamijo",
                "Gakuto Sōgetsu" => "Gakuto Sogetsu",
                "E.Honda" => "E Honda",
                "A.K.I" => "A K I",
                "Bandana Waddle Dee" => "Bandana Waddle Dee Winged Star",
                _ => OriginalName
            };

            ReturnName = ReturnName.Replace("Slider", "");
            ReturnName = ReturnName.Replace("R.O.B.", "R O B");

            ReturnName = ReturnName.Replace(".", "");
            ReturnName = ReturnName.Replace("'", " ");
            ReturnName = ReturnName.Replace("\"", "");

            ReturnName = ReturnName.Replace(" & ", " ");
            ReturnName = ReturnName.Replace(" - ", " ");

            return ReturnName.Trim();
        });
        url = new Lazy<string>(() =>
        {
            string url = default;
            // If the amiibo is an animal crossing card, look name up on site and get the first link
            if (type == "Card" && amiiboSeries == "Animal Crossing")
            {
                try
                {
                    // Look amiibo up
                    HtmlDocument AmiiboLookup = new();
                    AmiiboLookup.LoadHtml(
                        WebUtility.HtmlDecode(
                            Program.GetAmiilifeStringAsync("https://amiibo.life/search?q=" + characterName).Result
                        )
                    );

                    // Filter for card amiibo only and get url
                    foreach (HtmlNode item in AmiiboLookup.DocumentNode.SelectNodes("//ul[@class='figures-cards small-block-grid-2 medium-block-grid-4 large-block-grid-4']/li"))
                    {
                        if (item.ChildNodes[1].GetAttributeValue("href", string.Empty).Contains("cards"))
                        {
                            url = "https://amiibo.life" + item.ChildNodes[1].GetAttributeValue("href", string.Empty);
                            break;
                        }
                    }

                    return url;
                }
                catch (AggregateException ex) when (ex.InnerException is System.Net.Http.HttpRequestException httpEx && httpEx.StatusCode == HttpStatusCode.NotFound)
                {
                    // 404 错误：搜索页面不存在，使用默认 URL 格式
                    Debugger.Log($"404 Not Found when searching for Animal Crossing card: {characterName}", Debugger.DebugLevel.Warn);
                    // 返回一个基于角色名的默认 URL
                    return $"https://amiibo.life/amiibo/animal-crossing/{characterName.Replace(" ", "-").ToLower()}";
                }
                catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // 404 错误：搜索页面不存在，使用默认 URL 格式
                    Debugger.Log($"404 Not Found when searching for Animal Crossing card: {characterName}", Debugger.DebugLevel.Warn);
                    // 返回一个基于角色名的默认 URL
                    return $"https://amiibo.life/amiibo/animal-crossing/{characterName.Replace(" ", "-").ToLower()}";
                }
            }
            else
            {
                string finalUrl;
                string GameSeriesURL = amiiboSeries.ToLower();
                GameSeriesURL = Regex.Replace(GameSeriesURL, @"[!.]", "");
                GameSeriesURL = Regex.Replace(GameSeriesURL, @"[' ]", "-");

                if (GameSeriesURL == "kirby-air-riders" && Name.ToLower().Contains("kirby"))
                {
                    finalUrl = "https://amiibo.life/amiibo/kirby-air-riders/kirby-warp-star";
                }
                else
                {
                    switch (Name.ToLower())
                    {
                        case "super mario cereal":
                            finalUrl = "https://amiibo.life/amiibo/super-mario-cereal/super-mario-cereal";
                            break;

                        case "solaire of astora":
                            finalUrl = "https://amiibo.life/amiibo/dark-souls/solaire-of-astora";
                            break;

                        default:
                            if (GameSeriesURL == "street-fighter-6")
                                GameSeriesURL = "street-fighter-6-starter-set";

                            finalUrl = $"https://amiibo.life/amiibo/{GameSeriesURL}/{Name.Replace(" ", "-").ToLower()}";

                            // Handle cat in getter for name
                            if (finalUrl.EndsWith("cat"))
                            {
                                finalUrl = finalUrl.Insert(finalUrl.LastIndexOf('/') + 1, "cat-")[..finalUrl.Length];
                            }
                            break;
                    }
                }

                // Debugger.Log($"GameSeriesURL: {GameSeriesURL}, Name: {Name}, URL: {finalUrl}", Debugger.DebugLevel.Verbose);
                return finalUrl;
            }
        });
    }

    public string URL => url.Value;

    /// <summary>Gets or sets the name.</summary>
    /// <value>The name.</value>
    public string Name
    {
        get => name.Value;
        set => OriginalName = value;
    }

    /// <summary>Gets the name of the character.</summary>
    /// <value>The name of the character.</value>
    public string characterName
    {
        get
        {
            string CharacterName = Program.BRootobject.rootobject.characters[$"0x{ID.ToString().ToLower().Substring(2, 4)}"];
            switch (CharacterName)
            {
                case "Spork/Crackle":
                    CharacterName = "Spork";
                    break;
                case "OHare":
                    CharacterName = "O'Hare";
                    break;
                default:
                    break;
            }

            return CharacterName;
        }
    }

    /// <summary>Gets the amiibo series.</summary>
    /// <value>The amiibo series.</value>
    public string amiiboSeries
    {
        get
        {
            string ID = $"0x{this.ID.ToString().Substring(14, 2)}";
            string AmiiboSeries = Program.BRootobject.rootobject.amiibo_series[ID.ToLower()];

            return AmiiboSeries switch
            {
                "8-bit Mario" => "Super Mario Bros 30th Anniversary",
                "Legend Of Zelda" => "The Legend Of Zelda",
                "Monster Hunter" => "Monster Hunter Stories",
                "Monster Sunter Stories Rise" => "Monster Hunter Rise",
                "Skylanders" => "Skylanders Superchargers",
                "Super Mario Bros." => "Super Mario",
                "Xenoblade Chronicles 3" => "Xenoblade Chronicles",
                "Yu-Gi-Oh!" => "Yu-Gi-Oh! Rush Duel Saikyo Battle Royale",
                _ => AmiiboSeries,
            };
        }
    }
    /// <summary>Gets the type.</summary>
    /// <value>The type.</value>
    public string type
    {
        get
        {
            string Type = Program.BRootobject.rootobject.types[$"0x{ID.ToString().Substring(8, 2)}"];
            return Type;
        }
    }
}
