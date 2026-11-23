namespace Still.Core.Presets;

/// <summary>
/// A theme such as browsers or games. Membership is not a hard-coded app list:
/// an installed application is matched against process names, precise display
/// name keywords and install path fragments. Matching stays deliberately
/// conservative, because a category should add the obvious apps, not every
/// helper executable that happens to sit in the same folder.
/// </summary>
public sealed record TargetCategory(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<string> Processes,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> PathFragments,
    IReadOnlyList<string> Websites)
{
    public bool MatchesProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var normalized = processName.Trim().ToLowerInvariant();
        if (normalized.EndsWith(".exe", StringComparison.Ordinal))
        {
            normalized = normalized[..^4];
        }

        return Processes.Contains(normalized);
    }

    public bool MatchesApp(string name, string processName, string path)
    {
        if (MatchesProcess(processName))
        {
            return true;
        }

        var normalizedName = name.ToLowerInvariant();
        foreach (var keyword in Keywords)
        {
            if (normalizedName.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var normalizedPath = path.ToLowerInvariant();
        foreach (var fragment in PathFragments)
        {
            if (normalizedPath.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public static class TargetCategories
{
    public static IReadOnlyList<TargetCategory> All { get; } =
    [
        new(
            "browser",
            "Browser",
            "Alles, womit man ins Netz kommt",
            ["chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc", "zen",
             "chromium", "yandex", "librewolf", "waterfox", "floorp", "thorium", "sidekick", "whale"],
            [],
            [],
            []),

        new(
            "games",
            "Spiele",
            "Launcher, Steam-Bibliothek und Spiel-Clients",
            ["steam", "epicgameslauncher", "riotclient", "riotclientservices", "battle.net", "origin",
             "eadesktop", "ubisoftconnect", "uplay", "galaxyclient", "rockstargameslauncher",
             "minecraft", "minecraftlauncher", "robloxplayerbeta", "robloxplayer", "leagueclient",
             "valorant", "cs2", "dota2", "fortniteclient-win64-shipping", "wow", "overwatch", "diablo iv"],
            ["minecraft", "roblox", "steam", "epic games", "riot games", "battle.net", "gog galaxy",
             "ubisoft connect", "rockstar", "league of legends", "valorant", "counter-strike",
             "fortnite", "world of warcraft", "overwatch", "diablo", "call of duty", "genshin", "the sims"],
            ["\\steamapps\\common\\", "\\epic games\\", "\\riot games\\", "\\battle.net\\", "\\gog galaxy\\",
             "\\ubisoft\\", "\\rockstar games\\", "\\minecraft", "\\roblox", "\\ea games\\"],
            ["roblox.com", "minecraft.net", "steampowered.com", "epicgames.com", "riotgames.com",
             "battle.net", "ea.com", "ubisoft.com", "rockstargames.com", "gog.com"]),

        new(
            "streaming",
            "Streaming",
            "Video, das schnell eine Stunde frisst",
            ["netflix", "disneyplus", "hbomax", "primevideo", "amazonprimevideo", "twitch",
             "paramountplus", "crunchyroll"],
            ["netflix", "disney+", "prime video", "twitch", "paramount+", "crunchyroll"],
            [],
            ["netflix.com", "disneyplus.com", "primevideo.com", "max.com", "twitch.tv", "joyn.de",
             "crunchyroll.com", "paramountplus.com"]),

        new(
            "social",
            "Social Media",
            "Feeds, Reels und Endlosschleifen",
            ["instagram", "tiktok", "twitter", "facebook", "snapchat", "threads", "bluesky", "pinterest"],
            ["instagram", "tiktok", "twitter", "facebook", "snapchat", "threads", "bluesky", "pinterest"],
            [],
            ["instagram.com", "tiktok.com", "x.com", "twitter.com", "facebook.com", "reddit.com",
             "snapchat.com", "threads.net", "bsky.app", "pinterest.com"]),

        new(
            "chat",
            "Chat & Meetings",
            "Nachrichten und Calls",
            ["whatsapp", "telegram", "signal", "discord", "slack", "teams", "ms-teams", "zoom",
             "skype", "element", "threema", "webex", "webexmta"],
            ["whatsapp", "telegram", "signal", "discord", "slack", "microsoft teams", "zoom",
             "skype", "threema", "element"],
            [],
            ["whatsapp.com", "web.whatsapp.com", "telegram.org", "web.telegram.org", "discord.com",
             "slack.com", "teams.microsoft.com", "zoom.us", "signal.org", "meet.google.com"]),

        new(
            "music",
            "Musik",
            "Player und Streaming-Dienste",
            ["spotify", "applemusic", "itunes", "tidal", "deezer", "soundcloud", "foobar2000",
             "aimp", "musicbee", "amazonmusic"],
            ["spotify", "apple music", "itunes", "tidal", "deezer", "soundcloud", "musicbee"],
            [],
            ["open.spotify.com", "music.youtube.com", "music.apple.com", "tidal.com", "deezer.com",
             "soundcloud.com", "bandcamp.com"]),

        new(
            "work",
            "Arbeit & Lernen",
            "Schreiben, Planen, Studium",
            ["winword", "excel", "powerpnt", "outlook", "onenote", "msaccess", "notion", "obsidian",
             "evernote", "code", "devenv", "idea64", "pycharm64", "webstorm64", "rider64", "goland64",
             "clion64", "figma", "miro", "soffice", "acrord32"],
            ["microsoft word", "microsoft excel", "powerpoint", "outlook", "onenote", "visual studio",
             "notion", "obsidian", "evernote", "figma", "miro", "libreoffice"],
            [],
            ["notion.so", "obsidian.md", "todoist.com", "trello.com", "figma.com", "miro.com",
             "docs.google.com", "calendar.google.com", "duolingo.com"]),

        new(
            "shopping",
            "Shopping",
            "Stöbern, das selten beim Nötigen endet",
            ["amazon", "ebay", "etsy", "zalando", "temu", "aliexpress"],
            ["amazon", "ebay", "etsy", "zalando", "temu", "aliexpress", "kleinanzeigen"],
            [],
            ["amazon.de", "ebay.de", "kleinanzeigen.de", "etsy.com", "zalando.de", "temu.com"])
    ];

    public static TargetCategory? Find(string key) => All.FirstOrDefault(c => c.Key == key);
}
