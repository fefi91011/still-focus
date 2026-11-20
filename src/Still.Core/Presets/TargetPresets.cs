using Still.Core.Models;

namespace Still.Core.Presets;

public sealed record TargetPreset(string Key, string Name, string Description, IReadOnlyList<Target> Targets);

public static class TargetPresets
{
    private static Target Site(string host) => new(TargetKind.Website, host);
    private static Target App(string process, string label) => new(TargetKind.Process, process, label);

    public static IReadOnlyList<TargetPreset> Web { get; } =
    [
        new("youtube", "YouTube", "Startseite, Shorts und Music",
        [
            Site("youtube.com"), Site("m.youtube.com"), Site("music.youtube.com"),
            Site("youtu.be"), Site("youtube-nocookie.com"), Site("youtubekids.com")
        ]),
        new("instagram", "Instagram", "Feed, Reels und Stories",
        [
            Site("instagram.com"), Site("instagr.am")
        ]),
        new("tiktok", "TikTok", "For-You-Feed und Kurzvideos",
        [
            Site("tiktok.com"), Site("vm.tiktok.com"), Site("vt.tiktok.com")
        ]),
        new("x", "X / Twitter", "Timeline und Kurznachrichten",
        [
            Site("x.com"), Site("twitter.com"), Site("t.co")
        ]),
        new("reddit", "Reddit", "Subreddits und Kommentare",
        [
            Site("reddit.com"), Site("old.reddit.com"), Site("redd.it")
        ]),
        new("netflix", "Netflix", "Streaming",
        [
            Site("netflix.com")
        ]),
        new("twitch", "Twitch", "Livestreams",
        [
            Site("twitch.tv")
        ]),
        new("discord", "Discord (Web)", "Chat und Sprachkanäle im Browser",
        [
            Site("discord.com"), Site("discord.gg"), Site("discordapp.com")
        ]),
        new("steam", "Steam (Web)", "Store und Community",
        [
            Site("steampowered.com"), Site("store.steampowered.com"), Site("steamcommunity.com")
        ]),
        new("whatsapp", "WhatsApp Web", "Web-Chat",
        [
            Site("whatsapp.com"), Site("web.whatsapp.com")
        ]),
        new("telegram", "Telegram (Web)", "Web-Chat",
        [
            Site("telegram.org"), Site("web.telegram.org"), Site("t.me")
        ]),
        new("pinterest", "Pinterest", "Pins und Boards",
        [
            Site("pinterest.com"), Site("pin.it")
        ])
    ];

    public static IReadOnlyList<TargetPreset> Apps { get; } =
    [
        new("all-browsers", "Alle Browser", "Sperrt Chrome, Edge, Firefox, Brave, Opera, Vivaldi, Arc und Zen",
        [
            App("chrome", "Chrome"), App("msedge", "Edge"), App("firefox", "Firefox"),
            App("brave", "Brave"), App("opera", "Opera"), App("vivaldi", "Vivaldi"),
            App("arc", "Arc"), App("zen", "Zen")
        ]),
        new("chrome", "Google Chrome", "Browser", [App("chrome", "Chrome")]),
        new("edge", "Microsoft Edge", "Browser", [App("msedge", "Edge")]),
        new("firefox", "Mozilla Firefox", "Browser", [App("firefox", "Firefox")]),
        new("brave", "Brave", "Browser", [App("brave", "Brave")]),
        new("discord", "Discord", "Desktop-App", [App("discord", "Discord")]),
        new("steam", "Steam", "Desktop-App", [App("steam", "Steam")]),
        new("epic", "Epic Games", "Launcher", [App("epicgameslauncher", "Epic Games")]),
        new("spotify", "Spotify", "Desktop-App", [App("spotify", "Spotify")]),
        new("telegram", "Telegram", "Desktop-App", [App("telegram", "Telegram")]),
        new("whatsapp", "WhatsApp", "Desktop-App", [App("whatsapp", "WhatsApp")]),
        new("signal", "Signal", "Desktop-App", [App("signal", "Signal")]),
        new("zoom", "Zoom", "Meetings", [App("zoom", "Zoom")]),
        new("minecraft", "Minecraft", "Spiel", [App("minecraft", "Minecraft")]),
        new("valorant", "VALORANT", "Spiel", [App("valorant", "VALORANT")]),
        new("cs2", "Counter-Strike 2", "Spiel", [App("cs2", "Counter-Strike 2")])
    ];

    public static IReadOnlyList<TargetPreset> All { get; } = Web.Concat(Apps).ToList();

    public static TargetPreset? Find(string key) => All.FirstOrDefault(p => p.Key == key);
}
