using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Navigator.Core.Localization;

namespace Navigator.Core.Tests;

public class LocalizationTests
{
    private static readonly ResourceManager Resources = new("Navigator.Core.Localization.Strings", typeof(Strings).Assembly);

    private static IEnumerable<string> Keys() =>
        typeof(Strings).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.GetMethod!.IsStatic)
            .Select(p => p.Name);

    public static TheoryData<string> Languages() => new(Strings.Languages);

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_text_is_translated_with_the_same_placeholders(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var italian = CultureInfo.GetCultureInfo("it");
        var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        foreach (var key in Keys())
        {
            var text = set!.GetString(key);
            Assert.False(string.IsNullOrEmpty(text), $"{language}: manca {key}");

            var expected = Regex.Matches(Resources.GetString(key, italian)!, @"\{\d[^}]*\}").Select(m => m.Value).Order();
            var actual = Regex.Matches(text!, @"\{\d[^}]*\}").Select(m => m.Value).Order();
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData("it", "Nuova cartella")]
    [InlineData("en", "New folder")]
    [InlineData("de", "Neuer Ordner")]
    [InlineData("fr", "Nouveau dossier")]
    [InlineData("es", "Nueva carpeta")]
    public void Texts_follow_the_language(string language, string newFolder) =>
        Assert.Equal(newFolder, Resources.GetString(nameof(Strings.NewFolder), CultureInfo.GetCultureInfo(language)));
}
