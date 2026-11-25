using System.Reflection;

namespace HotkeyLens.App.Services;

public static class AppVersion
{
    public static string Current
    {
        get
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "1.1.0";
            var plus = version.IndexOf('+');
            return plus > 0 ? version[..plus] : version;
        }
    }
}