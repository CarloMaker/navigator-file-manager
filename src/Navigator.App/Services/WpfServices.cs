using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Navigator.Core.Services;
using Navigator.Core.Localization;

namespace Navigator.App.Services;

public sealed class WpfDispatcher : IUiDispatcher
{
    public void Post(Action action) => Application.Current?.Dispatcher.InvokeAsync(action);
}

/// <summary>Appunti compatibili con Esplora file (Copia/Taglia/Incolla tra le due app).</summary>
public sealed class WpfClipboardService : IClipboardService
{
    private const string PreferredDropEffect = "Preferred DropEffect";
    private const int DropEffectCopy = 5; // COPY | LINK, come Esplora file
    private const int DropEffectMove = 2;

    public void SetFiles(IReadOnlyList<string> paths, bool cut)
    {
        var list = new StringCollection();
        list.AddRange(paths.ToArray());

        var data = new DataObject();
        data.SetFileDropList(list);
        data.SetData(PreferredDropEffect, new MemoryStream(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy)));
        Retry(() => Clipboard.SetDataObject(data, copy: true));
    }

    public (IReadOnlyList<string> Paths, bool IsCut) GetFiles()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return (Array.Empty<string>(), false);
            }

            var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            var isCut = false;
            if (Clipboard.GetData(PreferredDropEffect) is MemoryStream stream && stream.Length >= 4)
            {
                var buffer = new byte[4];
                stream.ReadExactly(buffer, 0, 4);
                isCut = (BitConverter.ToInt32(buffer, 0) & DropEffectMove) != 0;
            }

            return (paths, isCut);
        }
        catch (COMException)
        {
            return (Array.Empty<string>(), false);
        }
    }

    public bool HasFiles()
    {
        try
        {
            return Clipboard.ContainsFileDropList();
        }
        catch (COMException)
        {
            return false;
        }
    }

    public void SetText(string text) => Retry(() => Clipboard.SetText(text));

    /// <summary>Gli appunti possono essere occupati da un'altra app per qualche millisecondo.</summary>
    private static void Retry(Action action)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(50);
            }
        }
    }
}

public sealed class WpfDialogService : IDialogService
{
    public string? Prompt(string title, string message, string defaultValue)
    {
        var dialog = new InputDialog(title, message, defaultValue) { Owner = ActiveWindow() };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message) =>
        Show(message, "Navigator", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = ActiveWindow();
        return owner is null
            ? MessageBox.Show(message, title, buttons, image)
            : MessageBox.Show(owner, message, title, buttons, image);
    }

    private static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
}

/// <summary>Piccola finestra per inserire un nome (rinomina, nuova cartella, segnalibri, sessioni).</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _box;

    public InputDialog(string title, string message, string defaultValue)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Nav.Toolbar");
        SetResourceReference(ForegroundProperty, "Nav.Text");

        _box = new TextBox { Text = defaultValue, Margin = new Thickness(0, 8, 0, 16), MinHeight = 32 };

        var ok = new Button { Content = Strings.Ok, IsDefault = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true, MinWidth = 96 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = message });
        root.Children.Add(_box);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) =>
        {
            _box.Focus();
            // Come Esplora file: seleziona il nome senza l'estensione.
            var dot = _box.Text.LastIndexOf('.');
            if (dot > 0)
            {
                _box.Select(0, dot);
            }
            else
            {
                _box.SelectAll();
            }
        };
    }

    public string Value => _box.Text;
}
