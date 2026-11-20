using Still.Core.Runtime;
using Still.Platform.Null;
using Still.Platform.Windows;

namespace Still.Platform;

public static class PlatformFactory
{
    public static IPlatform Create() => OperatingSystem.IsWindows()
        ? new WindowsPlatform()
        : new NullPlatform();
}
