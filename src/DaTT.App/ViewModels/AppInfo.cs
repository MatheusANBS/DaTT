using System.Reflection;

namespace DaTT.App.ViewModels;

public static class AppInfo
{
    public static string Version =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
}
