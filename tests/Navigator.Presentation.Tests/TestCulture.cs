using System.Globalization;
using System.Runtime.CompilerServices;
using Navigator.Core.Localization;

namespace Navigator.Presentation.Tests;

internal static class TestCulture
{
    /// <summary>I test controllano i testi in italiano, qualunque sia la lingua del PC su cui girano.</summary>
    [ModuleInitializer]
    internal static void UseItalian() => Strings.Culture = CultureInfo.GetCultureInfo("it");
}
