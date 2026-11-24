using System.Diagnostics;
using System.Text;
using Navigator.Core.Models;
using Navigator.Core.Services;
using Navigator.Core.Utilities;

namespace Navigator.Infrastructure;

/// <summary>
/// Stato Git calcolato con git.exe (Git per Windows), senza TortoiseGit.
/// Solo lettura: git gira con --no-optional-locks e non modifica mai l'indice o il repository.
/// </summary>
public sealed class GitStatusService : IGitStatusService
{
    /// <summary>Cartelle-repository in una stessa cartella di cui calcolare il riepilogo.</summary>
    private const int MaxNestedRepositories = 64;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Lazy<string?> _git;

    public GitStatusService()
        : this(null)
    {
    }

    /// <param name="gitPath">Percorso di git; null = cercato nel PATH e nelle cartelle di installazione di Git per Windows.</param>
    public GitStatusService(string? gitPath) => _git = new Lazy<string?>(() => gitPath ?? FindGit());

    public bool IsAvailable => _git.Value is not null;

    public IReadOnlyDictionary<string, GitItemStatus> GetFolderStatus(string folder, IReadOnlyList<FileEntry> entries, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, GitItemStatus>(StringComparer.OrdinalIgnoreCase);
        if (_git.Value is not { } git || entries.Count == 0)
        {
            return result;
        }

        // La cartella fa parte di un repository: un solo git status limitato alla cartella.
        var root = FindRepositoryRoot(folder);
        if (root is not null)
        {
            var relative = Path.GetRelativePath(root, folder).Replace('\\', '/');
            if (relative == ".")
            {
                relative = string.Empty;
            }

            if (relative.Equals(".git", StringComparison.OrdinalIgnoreCase) || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
            {
                return result; // dentro la cartella .git
            }

            var output = Run(git, root, ["status", "--porcelain=v1", "-z", "--ignored=matching", "--untracked-files=normal", "--", relative.Length == 0 ? "." : relative], cancellationToken);
            if (output is not null)
            {
                var map = GitStatusParser.MapChildren(GitStatusParser.Parse(output), relative, entries.Select(e => (e.Name, e.IsDirectory)));
                foreach (var (name, status) in map)
                {
                    result[name] = status;
                }
            }
        }

        // Sottocartelle che sono a loro volta repository (o sottomoduli): riepilogo del loro stato.
        foreach (var entry in entries.Where(e => e.IsDirectory && IsRepositoryRoot(e.FullPath)).Take(MaxNestedRepositories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = Run(git, entry.FullPath, ["status", "--porcelain=v1", "-z", "--untracked-files=no"], cancellationToken);
            if (output is not null)
            {
                result[entry.Name] = GitStatusParser.Summarize(GitStatusParser.Parse(output));
            }
        }

        return result;
    }

    /// <summary>Prima cartella, risalendo da <paramref name="folder"/>, che contiene ".git" (cartella o file).</summary>
    public static string? FindRepositoryRoot(string folder)
    {
        try
        {
            for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
            {
                if (IsRepositoryRoot(dir.FullName))
                {
                    return dir.FullName;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
        }

        return null;
    }

    private static bool IsRepositoryRoot(string folder)
    {
        var git = Path.Combine(folder, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    /// <summary>Esegue git e restituisce lo standard output; null se git fallisce, va oltre il tempo massimo o viene annullato.</summary>
    private static string? Run(string git, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var psi = new ProcessStartInfo(git)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = workingDirectory,
        };
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("--literal-pathspecs");
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(workingDirectory);
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        using (cancellationToken.Register(() => TryKill(process)))
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                TryKill(process);
                return null;
            }

            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();
            _ = stderr.GetAwaiter().GetResult();
            var output = stdout.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output : null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    /// <summary>git.exe nel PATH, altrimenti nelle cartelle di installazione di Git per Windows.</summary>
    private static string? FindGit()
    {
        var exe = OperatingSystem.IsWindows() ? "git.exe" : "git";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), exe);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string?[] roots =
        [
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        ];

        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
        {
            var candidate = Path.Combine(root!, "Git", "cmd", "git.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
