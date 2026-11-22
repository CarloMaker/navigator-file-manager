using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using ICSharpCode.AvalonEdit.Highlighting;
using Navigator.Core.Localization;

namespace Navigator.App;

/// <summary>Editor di testo integrato con evidenziazione della sintassi (AvalonEdit).</summary>
public partial class EditorWindow : Window
{
    private readonly string _path;
    private Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private bool _dirty;

    public EditorWindow(string path)
    {
        InitializeComponent();
        _path = path;
        Load();
        Editor.TextChanged += (_, _) => SetDirty(true);
        Closing += OnClosing;
    }

    private void Load()
    {
        using (var reader = new StreamReader(_path, _encoding, detectEncodingFromByteOrderMarks: true))
        {
            Editor.Text = reader.ReadToEnd();
            _encoding = reader.CurrentEncoding;
        }

        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(_path));
        Editor.IsModified = false;
        SetDirty(false);
        StatusText.Text = Strings.Format(Strings.EditorStatus, _encoding.WebName.ToUpperInvariant(), Editor.Document.LineCount);
    }

    private void OnSaveExecuted(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) => Save();

    private bool Save()
    {
        try
        {
            File.WriteAllText(_path, Editor.Text, _encoding);
            SetDirty(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Strings.Format(Strings.SaveFailed, ex.Message), "Navigator", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        Title = (dirty ? "• " : string.Empty) + Strings.Format(Strings.EditorTitle, Path.GetFileName(_path));
    }

    private void OnWrapChanged(object sender, RoutedEventArgs e) => Editor.WordWrap = WrapToggle.IsChecked == true;

    /// <summary>Chiede se salvare le modifiche; false se l'utente annulla.</summary>
    public bool ConfirmDiscard()
    {
        if (!_dirty)
        {
            return true;
        }

        Activate();
        var answer = MessageBox.Show(this, Strings.Format(Strings.SaveChangesConfirm, Path.GetFileName(_path)), "Navigator",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => MarkClean(),
            _ => false,
        };
    }

    private bool MarkClean()
    {
        _dirty = false;
        return true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
        }
    }
}
