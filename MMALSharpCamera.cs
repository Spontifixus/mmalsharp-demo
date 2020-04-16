using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BufferDemo.Extensions;
using Microsoft.Extensions.Logging;
using MMALSharp;
using MMALSharp.Common;
using MMALSharp.Components;
using MMALSharp.Handlers;
using MMALSharp.Native;
using MMALSharp.Ports;

namespace BufferDemo
{
    public class MMALSharpCamera : IDisposable
    {
        private const int ImageStreak = 12;

        private readonly ILogger<MMALSharpCamera> log;

        private MMALCamera camera;
        private long imageCount;
        private MemoryStreamCaptureHandler memoryStreamHandler;
        private MMALImageEncoder imageEncoder;
        private MMALPortConfig portConfig;
        private MMALResizerComponent resizer;
        private MMALPortConfig resizerPortConfig;
        private MMALNullSinkComponent nullSink;

        public MMALSharpCamera(ILogger<MMALSharpCamera> log)
        {
            this.log = log;
        }

        public async Task EnableAsync(CancellationToken cancellationToken)
        {
            try
            {
                this.camera = MMALCamera.Instance;
                await ConfigureCameraForCurrentLightingAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Could not enable camera.");
            }
        }

        public Task DisableAsync(CancellationToken cancellationToken)
        {
            try
            {
                this.ClearCameraPipeline();
                log.LogInformation("Camera disabled successfully.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Could not disable camera.");
            }

            return Task.CompletedTask;
        }

        public async Task<bool> TakePhotoAsync(Stream output, CancellationToken cancellationToken)
        {
            try
            {
                this.imageCount++;

                log.LogInformation($"Capturing image {this.imageCount} of {ImageStreak} in current streak...");
                await CaptureImageAsync(output, cancellationToken);

                if (output.Length == 0)
                {
                    log.LogWarning("Captured stream is empty.");
                    return false;
                }

                if (this.imageCount % ImageStreak == 0)
                {
                    log.LogInformation("Image streak complete. Adjusting camera settings...");
                    await this.ConfigureCameraForCurrentLightingAsync(cancellationToken);
                }

                this.log.LogDebug("Image captured successfully.");
                return true;
            }
            catch (Exception ex)
            {
                this.log.LogError(ex, "Capturing image failed.");
                return false;
            }
        }

        private async Task CaptureImageAsync(Stream output, CancellationToken cancellationToken)
        {
            try
            {
                log.LogDebug("Processing camera output...");
                this.memoryStreamHandler.CurrentStream.Clear();
                await camera.ProcessAsync(camera.Camera.StillPort, cancellationToken);

                var imageDataLengthInKiB = this.memoryStreamHandler.CurrentStream.Length / 1024;
                this.log.LogDebug($"Fetching image data ({imageDataLengthInKiB} KiB)...");
                await this.memoryStreamHandler.CurrentStream.CopyAndResetAsync(output);

                this.log.LogDebug("Image captured successfully.");
            }
            catch (Exception ex)
            {
                this.log.LogError(ex, "Capturing image failed.");
            }
        }

        private async Task ConfigureCameraForCurrentLightingAsync(CancellationToken cancellationToken)
        {
            this.imageCount = 0;

            await ConfigureCameraAsync(TimeSpan.Zero, 0, cancellationToken);

            await using var imageStream = new MemoryStream();
            await this.CaptureImageAsync(imageStream, cancellationToken);

            var lightness = 1d;
            if (imageStream.Length > 0)
            {
                log.LogDebug("Calculating average lightness of captured image...");
                using var image = Image.FromStream(imageStream);
                lightness = image.CalculateAverageLightness();
                log.LogDebug($"Average lightness is {lightness}.");
            }
            else
            {
                log.LogWarning("CurrentStream is empty. Assuming lightness of 1.");
            }

            if (lightness <= 0.01d)
            {
                log.LogInformation("It's dark outside. Shooting with ISO 800 and two seconds.");
                await ConfigureCameraAsync(TimeSpan.FromSeconds(2), 800, cancellationToken);
            }
            else if (lightness <= 0.1)
            {
                log.LogInformation("It's quite dark outside. Shooting with ISO 800 and 1200 milliseconds.");
                await ConfigureCameraAsync(TimeSpan.FromSeconds(1.2), 800, cancellationToken);
            }
            else
            {
                log.LogInformation("Light is fine. Shooting with default parameters.");
                await ConfigureCameraAsync(TimeSpan.Zero, 0, cancellationToken);
            }
        }

        private async Task BuildCameraPipelineAsync(CancellationToken cancellationToken)
        {
            camera.ConfigureCameraSettings();

            this.imageEncoder = new MMALImageEncoder();
            this.memoryStreamHandler = new MemoryStreamCaptureHandler();
            this.resizer = new MMALResizerComponent();
            this.nullSink = new MMALNullSinkComponent();

            this.portConfig = new MMALPortConfig(MMALEncoding.JPEG, MMALEncoding.I420, 90);
            this.resizerPortConfig = new MMALPortConfig(MMALEncoding.I420, MMALEncoding.I420, 1024, 768, 0, 0, 0, false, null);

            this.imageEncoder.ConfigureOutputPort(this.portConfig, memoryStreamHandler);
            this.resizer.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), camera.Camera.StillPort, null)
                .ConfigureOutputPort(this.resizerPortConfig, null);

            camera.Camera.StillPort.ConnectTo(resizer);
            this.resizer.Outputs[0].ConnectTo(imageEncoder);
            camera.Camera.PreviewPort.ConnectTo(nullSink);

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        private void ClearCameraPipeline()
        {
            if (this.nullSink != null)
            {
                this.nullSink.Dispose();
                this.nullSink = null;
            }
            if (this.imageEncoder != null)
            {
                this.imageEncoder.Dispose();
                this.imageEncoder = null;
            }
            if (this.resizer != null)
            {
                this.resizer.Dispose();
                this.resizer = null;
            }
            if (this.memoryStreamHandler != null)
            {
                this.memoryStreamHandler.Dispose();
                this.memoryStreamHandler = null;
            }
        }

        private async Task ConfigureCameraAsync(TimeSpan shutterSpeed, int iso, CancellationToken cancellationToken)
        {
            log.LogDebug($"Configuring Camera... (ShutterSpeed={shutterSpeed:ss\\.fff}, ISO={iso})");
            ClearCameraPipeline();

            MMALCameraConfig.Rotation = 180;
            MMALCameraConfig.VideoFramerate = new MMAL_RATIONAL_T(0, 1);
            MMALCameraConfig.AwbMode = MMAL_PARAM_AWBMODE_T.MMAL_PARAM_AWBMODE_GREYWORLD;
            MMALCameraConfig.ShutterSpeed = (int)shutterSpeed.TotalMilliseconds * 1000;
            MMALCameraConfig.ISO = iso;

            await BuildCameraPipelineAsync(cancellationToken);
        }

        private void ReleaseUnmanagedResources()
        {
            MMALCamera.Instance.Cleanup();
            log.LogInformation("Unmanaged camera resources cleaned up.");
        }

        private void Dispose(bool disposing)
        {
            ReleaseUnmanagedResources();
            if (disposing)
            {
                memoryStreamHandler?.Dispose();
                imageEncoder?.Dispose();
                resizer?.Dispose();
                nullSink?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~MMALSharpCamera()
        {
            Dispose(false);
        }
    }
}
