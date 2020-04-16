using System.IO;
using System.Threading.Tasks;

namespace BufferDemo.Extensions
{
    public static class StreamExtensions
    {
        public static async Task CopyAndResetAsync(this Stream input, Stream output)
        {
            output.Clear();
            input.Rewind();

            await input.CopyToAsync(output);

            output.Rewind();
            input.Clear();
        }

        public static void Clear(this Stream input)
        {
            input.SetLength(0);
        }

        public static void Rewind(this Stream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }
    }
}
