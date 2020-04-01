using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MMALSharp;
using MMALSharp.Common;
using MMALSharp.Common.Utility;
using MMALSharp.Components;
using MMALSharp.Handlers;
using MMALSharp.Ports;

namespace BufferDemo
{
    class Program
    {
        static async Task Main()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole(o => o.DisableColors = true);
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            var log = loggerFactory.CreateLogger<Program>();
            MMALLog.LoggerFactory = loggerFactory;

            log.LogInformation("Configuring camera...");

            var camera = MMALCamera.Instance;
            camera.ConfigureCameraSettings();

            var imageEncoder = new MMALImageEncoder();
            var resizer = new MMALResizerComponent();

            var encoderPortConfig = new MMALPortConfig(MMALEncoding.JPEG, MMALEncoding.I420, 90);
            var resizerPortConfig = new MMALPortConfig(MMALEncoding.I420, MMALEncoding.I420, 1024, 768, 0, 0, 0, false, null);

            resizer.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), camera.Camera.StillPort,
                    null)
                .ConfigureOutputPort(resizerPortConfig, null);

            var nullSink = new MMALNullSinkComponent();

            camera.Camera.StillPort.ConnectTo(resizer);
            resizer.Outputs[0].ConnectTo(imageEncoder);
            camera.Camera.PreviewPort.ConnectTo(nullSink);
            log.LogInformation("Camera configuration completed.");

            for (var i = 0; i < 100; i++)
            {
                // Create a memory stream handler, so the image
                // data that was captured gets stored in a stream
                // for further handling.
                using var memoryStreamHandler = new MemoryStreamCaptureHandler();

                // Capture th image
                log.LogInformation("Capturing image...");
                imageEncoder.ConfigureOutputPort(encoderPortConfig, memoryStreamHandler);
                await camera.ProcessAsync(camera.Camera.StillPort);

                // Sometimes the stream is empty after capturing.
                // If that happens, we skip the storing the image
                // and continue by capturing the next image
                if (memoryStreamHandler.CurrentStream.Length == 0)
                {
                    log.LogWarning("Capturing image failed. Skipping iteration.");
                    continue;
                }
                log.LogInformation("Image captured successfully.");


                // Simulate Storage
                // In the real world code this gets uploaded
                // to an Azure blob storage - that's why I did
                // not use one of the other capture handlers here.
                log.LogInformation("Storing image...");
                if (!Directory.Exists("images"))
                    Directory.CreateDirectory("images");
                await using var fileStream = File.OpenWrite($"images/test{i}.jpg");
                memoryStreamHandler.CurrentStream.Seek(0, SeekOrigin.Begin);
                await memoryStreamHandler.CurrentStream.CopyToAsync(fileStream);
                log.LogInformation("Image stored successfully...");

                memoryStreamHandler.Dispose();
            }
        }
    }
}
