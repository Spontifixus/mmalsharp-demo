using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using MMALSharp.Common.Utility;

namespace BufferDemo
{
    class Program
    {
        const int CaptureIntervalInSeconds = 5;

        private static CancellationTokenSource tokenSource;
        private static int completedImageCount;
        private static int failedImageCount;

        static async Task Main(params string[] args)
        {
            var enableMMALLog = args.Contains("--mmallog");

            // Ensure to register the cancellation key (CTRL+C) so
            // we can properly shut down the application and release
            // the used resources.
            Console.CancelKeyPress += Console_OnCancelKeyPress;

            // Setup logging.
            // There is no DI-Container in this app, so we need
            // to create the loggers needed for the camera and
            // the storage components
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole(o =>
                {
                    o.DisableColors = true;
                    o.Format = ConsoleLoggerFormat.Systemd;
                    o.TimestampFormat = "dd HH:mm:ss.fff ";
                });
                builder.SetMinimumLevel(LogLevel.Information);
            });
            var log = loggerFactory.CreateLogger<Program>();
            var cameraLog = loggerFactory.CreateLogger<MMALSharpCamera>();
            var storageLog = loggerFactory.CreateLogger<Storage>();

            // Here we go!
            log.LogInformation("Hello!");

            // Setup the camera and storage components. And while
            // we're at it, lets configure the logger for MMALSharp.
            if (enableMMALLog)
            {
                MMALLog.LoggerFactory = loggerFactory;
            }
            var camera = new MMALSharpCamera(cameraLog);
            var storage = new Storage(storageLog);

            // This is the loop from the server that usually runs
            // the camera.
            tokenSource = new CancellationTokenSource();
            try
            {
                // This enables the camera. The configuration of the
                // camera is handled in the MMALSharpCamera class.
                await camera.EnableAsync(tokenSource.Token);

                while (!tokenSource.IsCancellationRequested)
                {
                    log.LogInformation($"Shot {completedImageCount} images so far. {failedImageCount} attempts failed.");

                    var shotWatch = Stopwatch.StartNew();
                    await using var memoryStream = new MemoryStream();

                    // Shoot the actual image. If this is the 12th image,
                    // the camera pipeline will be reconfigured.
                    var imageWasShot = await camera.TakePhotoAsync(memoryStream, tokenSource.Token);
                    if (!imageWasShot)
                    {
                        failedImageCount++;
                        continue;
                    }

                    // Give the program the chance to exit
                    if (tokenSource.IsCancellationRequested) break;

                    // Store the files on disk. In the real world they
                    // get uploaded to an Azure blob store.
                    var imageWasStored = await storage.StoreImageAsync(memoryStream, tokenSource.Token);
                    if (!imageWasStored)
                    {
                        failedImageCount++;
                        continue;
                    }

                    completedImageCount++;

                    // Now wait for a certain amount of time, so that there
                    // are about 5 seconds between each capture.
                    shotWatch.Stop();
                    var delay = Math.Max(0, CaptureIntervalInSeconds * 1000 - shotWatch.ElapsedMilliseconds);
                    log.LogInformation($"Waiting {delay} ms before next shot...");
                    await Task.Delay((int)delay, tokenSource.Token);
                }
            }
            catch (TaskCanceledException)
            {
                log.LogWarning("Image capturing thread was aborted.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Image capturing thread failed unexpectedly.");
            }
            finally
            {
                // Use a new cancellation token source to actually shut down the application.
                tokenSource = new CancellationTokenSource();
                await camera.DisableAsync(tokenSource.Token);
                camera.Dispose();
            }

            log.LogInformation("Goodbye.");
        }

        private static void Console_OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            tokenSource?.Cancel();
        }
    }
}
