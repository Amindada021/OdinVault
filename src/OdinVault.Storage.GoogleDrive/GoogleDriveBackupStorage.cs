using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Upload;
using OdinVault.Core;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace OdinVault.Storage.GoogleDrive;

public sealed class GoogleDriveBackupStorage(
    DriveService driveService,
    string? folderId = null) : IBackupStorageProvider
{
    public StorageProviderType Type => StorageProviderType.GoogleDrive;

    public async Task<StorageUploadResult> UploadAsync(
        StorageUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var localFile = new FileInfo(request.LocalPath);
        if (!localFile.Exists)
            throw new FileNotFoundException("Backup file was not found.", request.LocalPath);

        var metadata = new DriveFile
        {
            Name = request.FileName,
            Parents = string.IsNullOrWhiteSpace(folderId) ? null : [folderId]
        };

        await using var stream = new FileStream(
            request.LocalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var upload = driveService.Files.Create(metadata, stream, "application/octet-stream");
        upload.Fields = "id,name,size,parents";
        upload.ChunkSize = 8 * 1024 * 1024;

        var progress = await upload.UploadAsync(cancellationToken);
        if (progress.Status != UploadStatus.Completed || upload.ResponseBody is null)
            throw progress.Exception ?? new IOException("Google Drive resumable upload did not complete.");

        var remote = upload.ResponseBody;
        return new StorageUploadResult(
            "google-drive",
            remote.Id,
            remote.Name,
            remote.Size ?? localFile.Length);
    }

    public async Task DownloadToAsync(
        string remoteId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var request = driveService.Files.Get(remoteId);
        var progress = await request.DownloadAsync(destination, cancellationToken);
        if (progress.Status != DownloadStatus.Completed)
            throw progress.Exception ?? new IOException("Google Drive download did not complete.");
    }

    public async Task DeleteAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        await driveService.Files.Delete(remoteId).ExecuteAsync(cancellationToken);
    }
}
