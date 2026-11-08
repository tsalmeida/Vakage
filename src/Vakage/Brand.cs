using System.Reflection;

namespace Vakage;

internal static class Brand
{
    public const string ProductName = "Vakage";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
}
