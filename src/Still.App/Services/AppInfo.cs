namespace Still.App.Services;

public static class AppInfo
{
    /// <summary>The version from the assembly, e.g. "0.1.0".</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var version = typeof(AppInfo).Assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
