using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MMALSharp;
using MMALSharp.Common;
using MMALSharp.Common.Utility;
using MMALSharp.Components;
using MMALSharp.Handlers;
using MMALSharp.Native;
using MMALSharp.Ports;

namespace BufferDemo
{
    class Program
    {
        private static ILogger<Program> log;
        private static MMALImageEncoder imageEncoder;
        private static MMALResizerComponent resizer;
        private static IMMALPortConfig encoderPortConfig;
        private static MMALPortConfig resizerPortConfig;
        private static MMALNullSinkComponent nullSink;
        private static MemoryStreamCaptureHandler memoryStreamCaptureHandler;

        static async Task Main()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole(o => o.DisableColors = true);
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            log = loggerFactory.CreateLogger<Program>();
            try
            {
                MMALCameraConfig.Debug = true;
                MMALLog.LoggerFactory = loggerFactory;

                log.LogInformation("Capturing first image...");
                MMALCameraConfig.Rotation = 180;
                await PreparePipeline();
                await CaptureImage(
                    1); // Usually this would run in a loop and capture a bunch of images with the same config.
                log.LogInformation("Captured first image.");


                log.LogInformation("Capturing second image...");
                ClearPipeline();
                MMALCameraConfig.Rotation = 180;
                MMALCameraConfig.ISO = 800;
                MMALCameraConfig.ShutterSpeed = 2000 * 1000;
                await PreparePipeline();
                await CaptureImage(
                    2); // Usually this would run in a loop and capture a bunch of images with the same config.
                log.LogInformation("Captured second image.");

                log.LogInformation("Capturing third image...");
                ClearPipeline();
                MMALCameraConfig.Rotation = 180;
                MMALCameraConfig.ISO = 0;
                MMALCameraConfig.ShutterSpeed = 0;
                await PreparePipeline();
                await CaptureImage(
                    3); // Usually this would run in a loop and capture a bunch of images with the same config.
                log.LogInformation("Captured third image.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Here goes the exception!");
            }
        }

        private static async Task CaptureImage(int index)
        {
            // Create a memory stream handler, so the image
            // data that was captured gets stored in a stream
            // for further handling.

            // Capture th image
            log.LogInformation("Capturing image...");
            await MMALCamera.Instance.ProcessAsync(MMALCamera.Instance.Camera.StillPort);

            // Sometimes the stream is empty after capturing.
            // If that happens, we skip the storing the image
            // and continue by capturing the next image
            if (memoryStreamCaptureHandler.CurrentStream.Length == 0)
            {
                log.LogWarning("Capturing image failed. Skipping iteration.");
                return;
            }
            log.LogInformation("Image captured successfully.");

            // Simulate Storage
            // In the real world code this gets uploaded
            // to an Azure blob storage - that's why I did
            // not use one of the other capture handlers here.
            log.LogInformation("Storing image...");
            if (!Directory.Exists("images"))
                Directory.CreateDirectory("images");
            await using var fileStream = File.OpenWrite($"images/test{index}.jpg");
            
            memoryStreamCaptureHandler.CurrentStream.Seek(0, SeekOrigin.Begin);
            await memoryStreamCaptureHandler.CurrentStream.CopyToAsync(fileStream);

            memoryStreamCaptureHandler.CurrentStream.Seek(0, SeekOrigin.Begin);
            memoryStreamCaptureHandler.CurrentStream.SetLength(0);

            log.LogInformation("Image stored successfully...");
        }

        private static async Task PreparePipeline()
        {
            log.LogInformation("Preparing camera pipeline...");
            
            memoryStreamCaptureHandler = new MemoryStreamCaptureHandler();

            imageEncoder = new MMALImageEncoder();
            resizer = new MMALResizerComponent();
            nullSink = new MMALNullSinkComponent();

            MMALCamera.Instance.ConfigureCameraSettings();

            encoderPortConfig = new MMALPortConfig(MMALEncoding.JPEG, MMALEncoding.I420, 90);
            resizerPortConfig = new MMALPortConfig(MMALEncoding.I420, MMALEncoding.I420, 1024, 768, 0, 0, 0, false, null);

            imageEncoder.ConfigureOutputPort(encoderPortConfig, memoryStreamCaptureHandler);
            resizer.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), MMALCamera.Instance.Camera.StillPort, null)
                   .ConfigureOutputPort(resizerPortConfig, null);

            MMALCamera.Instance.Camera.StillPort.ConnectTo(resizer);
            resizer.Outputs[0].ConnectTo(imageEncoder);
            MMALCamera.Instance.Camera.PreviewPort.ConnectTo(nullSink);

            log.LogInformation("Pipeline preparation completed.");

            await Task.Delay(2000);
        }

        private static void ClearPipeline()
        {
            log.LogDebug("Disposing of old parts...");
            imageEncoder?.Dispose();
            resizer?.Dispose();
            nullSink?.Dispose();
            memoryStreamCaptureHandler?.Dispose();
        }
    }
}
