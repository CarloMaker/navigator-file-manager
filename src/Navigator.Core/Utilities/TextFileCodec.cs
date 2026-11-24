using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace Navigator.Core.Utilities;

/// <summary>Contenuto di un file di testo letto da <see cref="TextFileCodec"/>.</summary>
/// <param name="Text">Testo decodificato.</param>
/// <param name="Encoding">Codifica rilevata, da riusare per salvare (BOM compreso).</param>
/// <param name="IsComplete">False se è stato letto solo l'inizio del file.</param>
public sealed record TextFileContent(string Text, Encoding Encoding, bool IsComplete);

/// <summary>
/// Lettura e scrittura dei file di testo mantenendo la codifica originale: BOM UTF-8/16/32,
/// UTF-8 senza BOM oppure la code page ANSI di sistema (es. Windows-1252) per i file che non sono UTF-8 validi.
/// </summary>
public static class TextFileCodec
{
    private const int SniffBytes = 8192;

    static TextFileCodec() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Legge al massimo <paramref name="maxBytes"/> byte. Con <paramref name="requireText"/> restituisce null
    /// se il file sembra binario (byte nulli senza BOM). Le eccezioni di I/O passano al chiamante.
    /// </summary>
    public static TextFileContent? Read(string path, long maxBytes, bool requireText)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var toRead = (int)Math.Min(Math.Max(0, maxBytes), Math.Min(length, int.MaxValue - 64));
        var bytes = new byte[toRead];
        var read = 0;
        while (read < toRead)
        {
            var n = stream.Read(bytes, read, toRead - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        var isComplete = read >= length;
        var data = bytes.AsSpan(0, read);
        var encoding = DetectEncoding(data, isComplete, out var bomLength);
        if (requireText && bomLength == 0 && data[..Math.Min(SniffBytes, data.Length)].IndexOf((byte)0) >= 0)
        {
            return null; // byte nulli: file binario
        }

        var text = encoding.GetString(data[bomLength..]);
        return new TextFileContent(text, encoding, isComplete);
    }

    /// <summary>Riconosce la codifica dai primi byte; <paramref name="bomLength"/> è la lunghezza del BOM da saltare.</summary>
    public static Encoding DetectEncoding(ReadOnlySpan<byte> data, bool isComplete, out int bomLength)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            bomLength = 3;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
        {
            bomLength = 4;
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        {
            bomLength = 4;
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            bomLength = 2;
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            bomLength = 2;
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        }

        bomLength = 0;

        // Se il file è stato letto solo in parte, l'ultimo carattere UTF-8 può essere tagliato a metà.
        var check = isComplete ? data : data[..Math.Max(0, data.Length - 3)];
        return Utf8.IsValid(check) ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : AnsiEncoding();
    }

    /// <summary>
    /// Scrive il testo con la codifica indicata (BOM compreso se la codifica lo prevede).
    /// Il file esistente viene troncato e riscritto, così restano attributi, permessi e file nascosti.
    /// </summary>
    public static void Write(string path, string text, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        stream.SetLength(0);
        stream.Write(preamble);
        stream.Write(body);
    }

    /// <summary>Nome breve della codifica per la barra di stato (es. "UTF-8", "UTF-8 BOM", "UTF-16 LE", "WINDOWS-1252").</summary>
    public static string Describe(Encoding encoding)
    {
        var bom = encoding.GetPreamble().Length > 0;
        return encoding switch
        {
            UTF8Encoding => bom ? "UTF-8 BOM" : "UTF-8",
            UnicodeEncoding => encoding.CodePage == 1201 ? "UTF-16 BE" : "UTF-16 LE",
            UTF32Encoding => encoding.CodePage == 12001 ? "UTF-32 BE" : "UTF-32 LE",
            _ => encoding.WebName.ToUpperInvariant(),
        };
    }

    private static Encoding AnsiEncoding()
    {
        try
        {
            var codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            return Encoding.GetEncoding(codePage is 0 or 65001 ? 1252 : codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }
}
