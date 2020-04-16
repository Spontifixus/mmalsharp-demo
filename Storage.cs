using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BufferDemo.Extensions;
using Microsoft.Extensions.Logging;

namespace BufferDemo
{
    public class Storage
    {
        private readonly ILogger<Storage> log;

        public Storage(ILogger<Storage> log)
        {
            this.log = log;
        }

        public ImageInfo LastStatus { get; } = new ImageInfo();

        public async Task<bool> StoreImageAsync(MemoryStream input, CancellationToken cancellationToken)
        {
            var isPrimary = !this.LastStatus.IsPrimary;

            var fileType = isPrimary ? "primary" : "secondary";
            try
            {
                var imageDataLengthInKiB = input.Length / 1024;
                log.LogDebug($"Storing {fileType} image ({imageDataLengthInKiB} KiB)...");

                var fileName = isPrimary ? FileNames.PrimaryImage : FileNames.SecondaryImage;
                var imageWasUploadedSuccessfully = await UploadFile(input, fileName, cancellationToken);
                if (!imageWasUploadedSuccessfully)
                {
                    log.LogWarning($"Uploading {fileName} failed.");
                    return false;
                }
                log.LogDebug($"Stored {fileType} image.");

                log.LogDebug("Storing status file...");
                this.LastStatus.IsPrimary = isPrimary;
                this.LastStatus.Timestamp = DateTime.Now;

                var statusFileJson = JsonSerializer.Serialize(this.LastStatus);
                var statusFileBytes = Encoding.UTF8.GetBytes(statusFileJson);
                await using var statusFileMemoryStream = new MemoryStream(statusFileBytes);
                var statusFileStoredSuccessfully = await UploadFile(statusFileMemoryStream, FileNames.StatusFile, cancellationToken);
                if (!statusFileStoredSuccessfully)
                {
                    log.LogWarning("Could not store status file.");
                    return false;
                }
                log.LogDebug("Stored status file.");

                return true;
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Storing {fileType} image failed.");
                return false;
            }
        }

        private async Task<bool> UploadFile(Stream content, string fileName, CancellationToken cancellationToken)
        {
            var fileSize = content.Length >= 1024
                ? $"{content.Length / 1024} kiB"
                : $"{content.Length} B";
            log.LogDebug($"Uploading {fileSize} to {fileName}...");

            content.Rewind();

            if (!Directory.Exists("output"))
            {
                Directory.CreateDirectory("output");
            }

            await using var fileHandle = File.OpenWrite(fileName);
            await content.CopyAndResetAsync(fileHandle);

            log.LogDebug($"Successfully stored {fileName}.");
            return true;
        }
    }
}
