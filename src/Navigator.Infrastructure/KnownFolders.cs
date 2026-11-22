using Navigator.Core.Services;
using Navigator.Core.Localization;

namespace Navigator.Infrastructure;

/// <summary>Cartelle di "Accesso rapido", come nel riquadro di spostamento di Esplora file.</summary>
public sealed class KnownFolders : IKnownFolders
{
    public string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public IReadOnlyList<(string Name, string Path)> GetQuickAccess()
    {
        var home = Home;
        var list = new List<(string, string)>
        {
            (Strings.KnownDesktop, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            (Strings.KnownDownloads, System.IO.Path.Combine(home, "Downloads")),
            (Strings.KnownDocuments, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            (Strings.KnownPictures, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            (Strings.KnownMusic, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            (Strings.KnownVideos, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        };

        return list.Where(f => !string.IsNullOrEmpty(f.Item2) && Directory.Exists(f.Item2)).ToList();
    }
}
