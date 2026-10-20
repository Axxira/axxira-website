using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RaidPilot.Overlay;

internal sealed record ScreenshotCleanupResult(
    int CandidateCount,
    int DeletedCount,
    int SkippedNewerCount,
    int ChangedIdentityCount,
    int MissingCount,
    int FailedCount,
    int RetryCount,
    IReadOnlyList<string> DeletedFileNames,
    IReadOnlyList<string> ResidualFileNames)
{
    internal static ScreenshotCleanupResult Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        Array.Empty<string>(),
        Array.Empty<string>());

    internal static ScreenshotCleanupResult FatalFailure() => new(
        0,
        0,
        0,
        0,
        0,
        1,
        0,
        Array.Empty<string>(),
        Array.Empty<string>());
}

/// <summary>
/// Test-only timing hooks. Production never supplies these callbacks.
/// </summary>
internal sealed class ScreenshotCleanupTestHooks
{
    internal Action<IReadOnlyList<string>>? AfterCandidatesCaptured { get; init; }
    internal Action<string, int>? BeforeDeleteAttempt { get; init; }
    internal Func<int, TimeSpan>? RetryDelay { get; init; }
}

internal static class ScreenshotFolderCleaner
{
    private static readonly TimeSpan StartupRaceSafetyMargin = TimeSpan.FromSeconds(3);
    private const uint DeleteAccess = 0x00010000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int FileDispositionInfo = 4;

    private readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileId);

    private readonly record struct FileSnapshot(
        string FileName,
        FileIdentity Identity,
        long CreationTime,
        long LastWriteTime,
        ulong Length);

    internal static bool TryGetProductionPaths(
        out string myDocumentsPath,
        out string screenshotsPath)
    {
        myDocumentsPath = Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolderOption.DoNotVerify);
        if (!TryResolveScreenshotsPath(myDocumentsPath, out screenshotsPath))
        {
            myDocumentsPath = string.Empty;
            screenshotsPath = string.Empty;
            return false;
        }

        myDocumentsPath = NormalizeDirectoryPath(myDocumentsPath);
        return true;
    }

    /// <summary>
    /// Derives the only cleanup target from one trusted Documents root. A
    /// caller cannot bless an arbitrary target by passing it a second time.
    /// </summary>
    internal static bool TryResolveScreenshotsPath(
        string? myDocumentsPath,
        out string screenshotsPath)
    {
        screenshotsPath = string.Empty;
        if (string.IsNullOrWhiteSpace(myDocumentsPath)
            || !Path.IsPathFullyQualified(myDocumentsPath))
        {
            return false;
        }

        try
        {
            var documents = NormalizeDirectoryPath(myDocumentsPath);
            var root = Path.GetPathRoot(documents);
            if (string.IsNullOrWhiteSpace(root)
                || string.Equals(
                    documents,
                    NormalizeDirectoryPath(root),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            screenshotsPath = Path.Combine(
                documents,
                "Escape from Tarkov",
                "Screenshots");
            return Path.IsPathFullyQualified(screenshotsPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    internal static ScreenshotCleanupResult ExecuteForDocumentsRoot(
        string myDocumentsPath,
        DateTimeOffset startupCutoffUtc,
        int maximumAttempts = 3,
        ScreenshotCleanupTestHooks? testHooks = null)
    {
        if (maximumAttempts is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        if (!TryResolveScreenshotsPath(myDocumentsPath, out var screenshotsPath))
        {
            return ScreenshotCleanupResult.FatalFailure();
        }

        var eftPath = Path.GetDirectoryName(screenshotsPath);
        if (string.IsNullOrWhiteSpace(eftPath))
        {
            return ScreenshotCleanupResult.FatalFailure();
        }

        // Documents redirection is trusted. The two application-owned child
        // components are opened as reparse points and identified by handle.
        // Their identities are revalidated around every candidate open.
        FileIdentity eftIdentity;
        using (var eftHandle = OpenDirectoryForIdentity(eftPath))
        {
            if (eftHandle.IsInvalid)
            {
                return IsMissingPathError(Marshal.GetLastWin32Error())
                    ? ScreenshotCleanupResult.Empty
                    : ScreenshotCleanupResult.FatalFailure();
            }
            if (!TryReadHandleInfo(eftHandle, out var eftInfo)
                || !IsSafeCleanupDirectoryAttributes((FileAttributes)eftInfo.FileAttributes))
            {
                return ScreenshotCleanupResult.FatalFailure();
            }
            eftIdentity = CreateIdentity(eftInfo);
        }

        FileIdentity screenshotsIdentity;
        using (var screenshotsHandle = OpenDirectoryForIdentity(screenshotsPath))
        {
            if (screenshotsHandle.IsInvalid)
            {
                return IsMissingPathError(Marshal.GetLastWin32Error())
                    ? ScreenshotCleanupResult.Empty
                    : ScreenshotCleanupResult.FatalFailure();
            }
            if (!TryReadHandleInfo(screenshotsHandle, out var screenshotsInfo)
                || !IsSafeCleanupDirectoryAttributes((FileAttributes)screenshotsInfo.FileAttributes))
            {
                return ScreenshotCleanupResult.FatalFailure();
            }
            screenshotsIdentity = CreateIdentity(screenshotsInfo);
        }

        var candidates = new List<FileSnapshot>();
        var deletedFileNames = new List<string>();
        var residualFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedNewer = 0;
        var changedIdentity = 0;
        var missing = 0;
        var failed = 0;
        var retries = 0;
        // NTFS is precise, but other supported filesystems may round write
        // times. Keeping a small pre-start margin makes a just-created raid
        // screenshot fail closed even on coarse timestamp storage.
        var startupCutoffFileTime = (startupCutoffUtc - StartupRaceSafetyMargin)
            .UtcDateTime
            .ToFileTimeUtc();

        try
        {
            foreach (var path in Directory.EnumerateFiles(
                screenshotsPath,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(path);
                if (!IsSafePngFileName(fileName))
                {
                    continue;
                }

                using var captureHandle = OpenCandidate(
                    path,
                    FileReadAttributes,
                    FileShareRead | FileShareWrite | FileShareDelete);
                if (!TryReadHandleInfo(captureHandle, out var info)
                    || !IsSafeCleanupFileAttributes((FileAttributes)info.FileAttributes))
                {
                    failed++;
                    residualFileNames.Add(fileName);
                    continue;
                }

                var snapshot = CreateSnapshot(fileName, info);
                if (snapshot.CreationTime >= startupCutoffFileTime
                    || snapshot.LastWriteTime >= startupCutoffFileTime)
                {
                    skippedNewer++;
                    continue;
                }

                candidates.Add(snapshot);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or NotSupportedException)
        {
            // The fixed list collected so far remains the only deletion set.
            // Never re-enumerate after an error because new raid files may now
            // exist under names that were not present at startup.
            failed++;
        }

        var candidateNames = candidates.Select(candidate => candidate.FileName).ToArray();
        testHooks?.AfterCandidatesCaptured?.Invoke(candidateNames);

        foreach (var candidate in candidates)
        {
            var candidatePath = Path.Combine(screenshotsPath, candidate.FileName);
            var completed = false;
            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                testHooks?.BeforeDeleteAttempt?.Invoke(candidate.FileName, attempt);
                if (!DirectoryChainMatches(
                        eftPath,
                        eftIdentity,
                        screenshotsPath,
                        screenshotsIdentity))
                {
                    changedIdentity++;
                    residualFileNames.Add(candidate.FileName);
                    completed = true;
                    break;
                }

                using var deleteHandle = OpenCandidate(
                    candidatePath,
                    DeleteAccess | FileReadAttributes,
                    FileShareRead | FileShareWrite | FileShareDelete);

                if (deleteHandle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error is ErrorFileNotFound or ErrorPathNotFound)
                    {
                        missing++;
                        completed = true;
                        break;
                    }

                    if (attempt < maximumAttempts)
                    {
                        retries++;
                        WaitBeforeRetry(attempt, testHooks);
                        continue;
                    }

                    failed++;
                    residualFileNames.Add(candidate.FileName);
                    completed = true;
                    break;
                }

                if (!TryReadHandleInfo(deleteHandle, out var currentInfo))
                {
                    if (attempt < maximumAttempts)
                    {
                        retries++;
                        WaitBeforeRetry(attempt, testHooks);
                        continue;
                    }

                    failed++;
                    residualFileNames.Add(candidate.FileName);
                    completed = true;
                    break;
                }

                if (!IsSafeCleanupFileAttributes((FileAttributes)currentInfo.FileAttributes)
                    || !CreateIdentity(currentInfo).Equals(candidate.Identity))
                {
                    changedIdentity++;
                    residualFileNames.Add(candidate.FileName);
                    completed = true;
                    break;
                }

                // Revalidate the path chain after opening the exact file. A
                // swap to a junction or a directory containing a hard link to
                // the same file identity must still fail closed.
                if (!DirectoryChainMatches(
                        eftPath,
                        eftIdentity,
                        screenshotsPath,
                        screenshotsIdentity))
                {
                    changedIdentity++;
                    residualFileNames.Add(candidate.FileName);
                    completed = true;
                    break;
                }

                var current = CreateSnapshot(candidate.FileName, currentInfo);
                if (current.CreationTime != candidate.CreationTime
                    || current.LastWriteTime != candidate.LastWriteTime
                    || current.Length != candidate.Length)
                {
                    skippedNewer++;
                    residualFileNames.Add(candidate.FileName);
                    completed = true;
                    break;
                }

                var disposition = new FileDispositionInformation { DeleteFile = true };
                if (SetFileInformationByHandle(
                    deleteHandle,
                    FileDispositionInfo,
                    ref disposition,
                    (uint)Marshal.SizeOf<FileDispositionInformation>()))
                {
                    deletedFileNames.Add(candidate.FileName);
                    completed = true;
                    break;
                }

                if (attempt < maximumAttempts)
                {
                    retries++;
                    WaitBeforeRetry(attempt, testHooks);
                    continue;
                }

                failed++;
                residualFileNames.Add(candidate.FileName);
                completed = true;
                break;
            }

            if (!completed)
            {
                failed++;
                residualFileNames.Add(candidate.FileName);
            }
        }

        return new ScreenshotCleanupResult(
            candidates.Count,
            deletedFileNames.Count,
            skippedNewer,
            changedIdentity,
            missing,
            failed,
            retries,
            deletedFileNames.AsReadOnly(),
            residualFileNames.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static bool IsSafeCleanupDirectoryAttributes(FileAttributes attributes)
    {
        return (attributes & FileAttributes.Directory) != 0
            && (attributes & FileAttributes.ReparsePoint) == 0;
    }

    private static bool IsSafeCleanupFileAttributes(FileAttributes attributes)
    {
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }

    private static bool IsSafePngFileName(string? fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName)
            && string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            && string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingPathError(int error)
    {
        return error is ErrorFileNotFound or ErrorPathNotFound;
    }

    private static void WaitBeforeRetry(int completedAttempt, ScreenshotCleanupTestHooks? hooks)
    {
        var delay = hooks?.RetryDelay?.Invoke(completedAttempt)
            ?? TimeSpan.FromMilliseconds(completedAttempt == 1 ? 35 : 90);
        if (delay > TimeSpan.Zero)
        {
            Thread.Sleep(delay);
        }
    }

    private static bool DirectoryChainMatches(
        string eftPath,
        FileIdentity expectedEftIdentity,
        string screenshotsPath,
        FileIdentity expectedScreenshotsIdentity)
    {
        return DirectoryMatches(eftPath, expectedEftIdentity)
            && DirectoryMatches(screenshotsPath, expectedScreenshotsIdentity);
    }

    private static bool DirectoryMatches(string path, FileIdentity expectedIdentity)
    {
        using var handle = OpenDirectoryForIdentity(path);
        return TryReadHandleInfo(handle, out var information)
            && IsSafeCleanupDirectoryAttributes((FileAttributes)information.FileAttributes)
            && CreateIdentity(information).Equals(expectedIdentity);
    }

    private static SafeFileHandle OpenDirectoryForIdentity(string path)
    {
        return CreateFile(
            path,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            nint.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            nint.Zero);
    }

    private static SafeFileHandle OpenCandidate(string path, uint access, uint sharing)
    {
        return CreateFile(
            path,
            access,
            sharing,
            nint.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            nint.Zero);
    }

    private static bool TryReadHandleInfo(
        SafeFileHandle? handle,
        out ByHandleFileInformation information)
    {
        information = default;
        return handle is { IsInvalid: false, IsClosed: false }
            && GetFileInformationByHandle(handle, out information);
    }

    private static FileSnapshot CreateSnapshot(
        string fileName,
        ByHandleFileInformation information)
    {
        return new FileSnapshot(
            fileName,
            CreateIdentity(information),
            ToLong(information.CreationTimeHigh, information.CreationTimeLow),
            ToLong(information.LastWriteTimeHigh, information.LastWriteTimeLow),
            ((ulong)information.FileSizeHigh << 32) | information.FileSizeLow);
    }

    private static FileIdentity CreateIdentity(ByHandleFileInformation information)
    {
        return new FileIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }

    private static long ToLong(uint high, uint low)
    {
        return unchecked((long)(((ulong)high << 32) | low));
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal uint CreationTimeLow;
        internal uint CreationTimeHigh;
        internal uint LastAccessTimeLow;
        internal uint LastAccessTimeHigh;
        internal uint LastWriteTimeLow;
        internal uint LastWriteTimeHigh;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformation
    {
        [MarshalAs(UnmanagedType.Bool)]
        internal bool DeleteFile;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        ref FileDispositionInformation fileInformation,
        uint bufferSize);
}
