using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Navigator.Core.Models;
using Navigator.Core.Localization;

namespace Navigator.Presentation.ViewModels;

/// <summary>
/// Un elemento dell'albero dei segnalibri: un segnalibro (a una cartella o a un file)
/// oppure una cartella virtuale che ne raccoglie altri, come le categorie di CubicExplorer.
/// </summary>
public sealed partial class BookmarkItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    private BookmarkItemViewModel(string name, string? path)
    {
        _name = name;
        Path = path;
    }

    public static BookmarkItemViewModel CreateLink(string name, string path) => new(name, path);

    public static BookmarkItemViewModel CreateFolder(string name) => new(name, null);

    /// <summary>Percorso del segnalibro; null per una cartella virtuale.</summary>
    public string? Path { get; }

    public bool IsFolder => Path is null;

    /// <summary>Cartella virtuale che contiene l'elemento; null se è alla radice.</summary>
    public BookmarkItemViewModel? Parent { get; internal set; }

    public ObservableCollection<BookmarkItemViewModel> Children { get; } = [];

    /// <summary>Tutti gli elementi contenuti, a qualsiasi profondità.</summary>
    public IEnumerable<BookmarkItemViewModel> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var nested in child.Descendants())
            {
                yield return nested;
            }
        }
    }

    /// <summary>true se <paramref name="other"/> sta dentro questa cartella, a qualsiasi profondità.</summary>
    public bool IsAncestorOf(BookmarkItemViewModel? other)
    {
        for (var p = other?.Parent; p is not null; p = p.Parent)
        {
            if (ReferenceEquals(p, this))
            {
                return true;
            }
        }

        return false;
    }

    public static BookmarkItemViewModel FromNode(BookmarkNode node, BookmarkItemViewModel? parent)
    {
        // Un nodo con percorso è un segnalibro; senza percorso è una cartella virtuale.
        var item = string.IsNullOrWhiteSpace(node.Path)
            ? CreateFolder(string.IsNullOrWhiteSpace(node.Name) ? Strings.DefaultFolderName : node.Name)
            : CreateLink(string.IsNullOrWhiteSpace(node.Name) ? node.Path : node.Name, node.Path);

        item.Parent = parent;
        item.IsExpanded = node.IsExpanded;
        if (item.IsFolder && node.Children is not null)
        {
            foreach (var child in node.Children)
            {
                item.Children.Add(FromNode(child, item));
            }
        }

        return item;
    }

    public BookmarkNode ToNode() => IsFolder
        ? new BookmarkNode { Name = Name, IsExpanded = IsExpanded, Children = Children.Select(c => c.ToNode()).ToList() }
        : BookmarkNode.Link(Name, Path!);
}
