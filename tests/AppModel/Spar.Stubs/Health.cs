namespace Spar;

/// <summary>What the last run left behind.</summary>
public sealed record CrashRecord(DateTimeOffset At, string Exception, string AppVersion, string OsVersion);

/// <summary>Health of the app across runs; injectable. Spar never uploads anything by itself.</summary>
public interface IAppHealth
{
    /// <summary>The crash of the previous run, if there was one.</summary>
    CrashRecord? LastCrash { get; }

    /// <summary>Packs logs and crash records into one zip, written safely into a grant.</summary>
    Task<Result<FileGrant, FileAccessError>> CollectAsync(FolderGrant destination, string fileName, CancellationToken cancellationToken);
}
