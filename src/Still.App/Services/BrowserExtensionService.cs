using System.Diagnostics;
using System.IO.Compression;

namespace Still.App.Services;

public enum BrowserEngine
{
    Chromium,
    Firefox
}

public sealed record DetectedBrowser(string Name, BrowserEngine Engine, string ExecutablePath)
{
    public string EngineLabel => Engine == BrowserEngine.Chromium ? "Chromium" : "Firefox";
}

public sealed record BrowserExtensionPackage(string Directory, string ArchivePath, bool IsSigned);

public sealed record BrowserSetupResult(bool Ok, string Message)
{
    public static BrowserSetupResult Success(string message) => new(true, message);
    public static BrowserSetupResult Failure(string message) => new(false, message);
}

/// <summary>
/// Prepares the bundled WebExtension for a specific browser family and opens
/// the browser's extension page. Browsers do not allow a normal desktop app to
/// silently install an unpacked extension, so this is the closest safe flow:
/// one click prepares the files and opens the right page.
/// </summary>
public sealed class BrowserExtensionService
{
    private const string ExtensionFolderName = "browser-extension";

    private sealed class BrowserSpec
    {
        public BrowserSpec(string name, BrowserEngine engine, string executableName, params string[] directories)
        {
            Name = name;
            Engine = engine;
            ExecutableName = executableName;
            Directories = directories;
        }

        public string Name { get; }
        public BrowserEngine Engine { get; }
        public string ExecutableName { get; }
        public string[] Directories { get; }
    }

    private static readonly BrowserSpec[] Specs =
    [
        new("Google Chrome", BrowserEngine.Chromium, "chrome.exe",
            Path.Combine(ProgramFiles, "Google", "Chrome", "Application"),
            Path.Combine(ProgramFilesX86, "Google", "Chrome", "Application"),
            Path.Combine(LocalAppData, "Google", "Chrome", "Application")),

        new("Microsoft Edge", BrowserEngine.Chromium, "msedge.exe",
            Path.Combine(ProgramFilesX86, "Microsoft", "Edge", "Application"),
            Path.Combine(ProgramFiles, "Microsoft", "Edge", "Application")),

        new("Brave", BrowserEngine.Chromium, "brave.exe",
            Path.Combine(ProgramFiles, "BraveSoftware", "Brave-Browser", "Application"),
            Path.Combine(ProgramFilesX86, "BraveSoftware", "Brave-Browser", "Application"),
            Path.Combine(LocalAppData, "BraveSoftware", "Brave-Browser", "Application")),

        new("Vivaldi", BrowserEngine.Chromium, "vivaldi.exe",
            Path.Combine(LocalAppData, "Vivaldi", "Application"),
            Path.Combine(ProgramFiles, "Vivaldi", "Application")),

        new("Opera", BrowserEngine.Chromium, "opera.exe",
            Path.Combine(LocalAppData, "Programs", "Opera"),
            Path.Combine(ProgramFiles, "Opera")),

        new("Opera GX", BrowserEngine.Chromium, "opera.exe",
            Path.Combine(LocalAppData, "Programs", "Opera GX"),
            Path.Combine(ProgramFiles, "Opera GX")),

        new("Chromium", BrowserEngine.Chromium, "chrome.exe",
            Path.Combine(LocalAppData, "Chromium", "Application")),

        new("Arc", BrowserEngine.Chromium, "Arc.exe",
            Path.Combine(LocalAppData, "Programs", "Arc"),
            Path.Combine(ProgramFiles, "Arc")),

        new("Mozilla Firefox", BrowserEngine.Firefox, "firefox.exe",
            Path.Combine(ProgramFiles, "Mozilla Firefox"),
            Path.Combine(ProgramFilesX86, "Mozilla Firefox")),

        new("Zen Browser", BrowserEngine.Firefox, "zen.exe",
            Path.Combine(LocalAppData, "Programs", "Zen Browser"),
            Path.Combine(ProgramFiles, "Zen Browser")),

        new("LibreWolf", BrowserEngine.Firefox, "librewolf.exe",
            Path.Combine(ProgramFiles, "LibreWolf"),
            Path.Combine(LocalAppData, "Programs", "LibreWolf")),

        new("Waterfox", BrowserEngine.Firefox, "waterfox.exe",
            Path.Combine(ProgramFiles, "Waterfox")),

        new("Floorp", BrowserEngine.Firefox, "floorp.exe",
            Path.Combine(ProgramFiles, "Floorp"),
            Path.Combine(LocalAppData, "Programs", "Floorp"))
    ];

    private static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public IReadOnlyList<DetectedBrowser> DetectInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<DetectedBrowser>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var browsers = new List<DetectedBrowser>();

        foreach (var spec in Specs)
        {
            string? path = null;
            foreach (var directory in spec.Directories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                var candidate = Path.Combine(directory, spec.ExecutableName);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
            }

            if (path is null)
            {
                continue;
            }

            var fullPath = Path.GetFullPath(path);
            if (seen.Add(fullPath))
            {
                browsers.Add(new DetectedBrowser(spec.Name, spec.Engine, fullPath));
            }
        }

        return browsers
            .OrderBy(browser => browser.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Prepares the package and returns the instructions shown before opening the browser.</summary>
    public BrowserSetupResult Describe(DetectedBrowser browser)
    {
        if (!OperatingSystem.IsWindows())
        {
            return BrowserSetupResult.Failure(
                "Die Browser-Erkennung ist nur unter Windows verfügbar. " +
                "Die Erweiterung kann im Browser trotzdem manuell geladen werden.");
        }

        try
        {
            return BrowserSetupResult.Success(Instructions(browser, Prepare(browser.Engine)));
        }
        catch (Exception ex)
        {
            return BrowserSetupResult.Failure(
                "Der Browser-Schutz konnte nicht vorbereitet werden: " + ex.Message);
        }
    }

    /// <summary>Opens the browser extension page and the prepared package.</summary>
    public BrowserSetupResult Open(DetectedBrowser browser)
    {
        if (!OperatingSystem.IsWindows())
        {
            return BrowserSetupResult.Failure(
                "Die Browser-Erkennung ist nur unter Windows verfügbar.");
        }

        try
        {
            var package = Prepare(browser.Engine);
            if (browser.Engine == BrowserEngine.Chromium)
            {
                StartBrowser(browser, ExtensionsPage(browser));
                OpenFolder(package.Directory);
            }
            else if (package.IsSigned)
            {
                StartBrowser(browser, $"\"{package.ArchivePath}\"");
            }
            else
            {
                StartBrowser(browser, "about:debugging#/runtime/this-firefox");
                OpenFolder(package.Directory);
            }

            return BrowserSetupResult.Success($"{browser.Name} wurde geöffnet.");
        }
        catch (Exception ex)
        {
            return BrowserSetupResult.Failure(
                "Der Browser konnte nicht geöffnet werden: " + ex.Message);
        }
    }

    private static string Instructions(DetectedBrowser browser, BrowserExtensionPackage package)
    {
        if (browser.Engine == BrowserEngine.Chromium)
        {
            return
                $"Still öffnet gleich {browser.Name} und den vorbereiteten Ordner.\n\n" +
                "1. Aktiviere oben rechts den Entwicklermodus.\n" +
                "2. Wähle „Entpackte Erweiterung laden“.\n" +
                "3. Nimm den dann geöffneten Ordner:\n" +
                $"{package.Directory}\n\n" +
                "Danach verbindet sich die Erweiterung automatisch mit Still und zeigt " +
                "dieselbe Sperrkarte wie bei gesperrten Apps.";
        }

        if (package.IsSigned)
        {
            return
                $"Still öffnet gleich {browser.Name} mit dem Installationsdialog für die signierte Erweiterung.\n\n" +
                "Bestätige „Hinzufügen“. Danach verbindet sich die Erweiterung automatisch mit Still.";
        }

        return
            $"{browser.Name} verlangt für dauerhafte Erweiterungen eine Signatur von Mozilla. " +
            "Zum Testen bereitet Still die Erweiterung vor:\n\n" +
            "1. Wähle in der dann geöffneten Firefox-Seite „Temporäres Add-on laden“.\n" +
            "2. Öffne im dann geöffneten Ordner die Datei manifest.json.\n\n" +
            "Für eine dauerhafte Installation muss die XPI über addons.mozilla.org signiert werden. " +
            "Die Blockliste funktioniert auch nach einem Browser-Neustart weiter, " +
            "die Erweiterung selbst muss nach einem Neustart erneut geladen werden.";
    }

    public string OpenExtensionFolder()
    {
        var root = RootDirectory();
        Directory.CreateDirectory(root);
        OpenFolder(root);
        return root;
    }

    /// <summary>
    /// Refreshes packages the user has already prepared so an unpacked
    /// extension picks up fixes after an app update. Does nothing until the
    /// user has run the browser setup once.
    /// </summary>
    public void RefreshPreparedPackages()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var engine in new[] { BrowserEngine.Chromium, BrowserEngine.Firefox })
        {
            var targetRoot = Path.Combine(
                RootDirectory(),
                engine == BrowserEngine.Chromium ? "chromium" : "firefox");
            if (!Directory.Exists(targetRoot))
            {
                continue;
            }

            try
            {
                Prepare(engine);
            }
            catch
            {
                // A refresh must never block startup. Settings → Browser-Schutz
                // can prepare the package again.
            }
        }
    }

    private static BrowserExtensionPackage Prepare(BrowserEngine engine)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "extension");
        var manifestName = engine == BrowserEngine.Chromium ? "chrome.json" : "firefox.json";
        var manifest = Path.Combine(source, "manifests", manifestName);
        if (!File.Exists(manifest))
        {
            throw new FileNotFoundException(
                "Die Browser-Erweiterung fehlt in dieser Installation.", manifest);
        }

        var targetRoot = Path.Combine(RootDirectory(), engine == BrowserEngine.Chromium ? "chromium" : "firefox");

        // Keep the folder stable: an unpacked extension may be loaded from it.
        // Overwriting the files is safe; deleting the folder would break the
        // browser's reference on the next setup run.
        Directory.CreateDirectory(targetRoot);
        CopyFiles(Path.Combine(source, "src"), targetRoot);
        File.Copy(manifest, Path.Combine(targetRoot, "manifest.json"), overwrite: true);

        var archiveName = engine == BrowserEngine.Chromium
            ? "still-website-blocker-chromium.zip"
            : "still-website-blocker-firefox.xpi";
        var archivePath = Path.Combine(RootDirectory(), archiveName);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(
            targetRoot,
            archivePath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false);

        var signed = engine == BrowserEngine.Firefox && IsSignedXpi(archivePath);
        return new BrowserExtensionPackage(targetRoot, archivePath, signed);
    }

    private static bool IsSignedXpi(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Any(entry =>
                entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)
                && (entry.FullName.EndsWith(".rsa", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.EndsWith("cose.sig", StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return false;
        }
    }

    private static void CopyFiles(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string RootDirectory() =>
        Path.Combine(
            string.IsNullOrWhiteSpace(LocalAppData) ? Path.GetTempPath() : LocalAppData,
            "Still",
            ExtensionFolderName);

    private static string ExtensionsPage(DetectedBrowser browser) => browser.Name switch
    {
        "Microsoft Edge" => "edge://extensions/",
        "Brave" => "brave://extensions/",
        "Vivaldi" => "vivaldi://extensions/",
        "Opera" or "Opera GX" => "opera://extensions/",
        _ => "chrome://extensions/"
    };

    private static void StartBrowser(DetectedBrowser browser, string argument)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            Arguments = argument,
            UseShellExecute = true
        });
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // The path is also shown in the setup dialog.
        }
    }
}
