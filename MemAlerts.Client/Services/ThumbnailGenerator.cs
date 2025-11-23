using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MemAlerts.Client.Services;

public static class ThumbnailGenerator
{
    public static async Task<Uri> GenerateThumbnailAsync(string videoPath)
    {
        try
        {
            var tcs = new TaskCompletionSource<Uri>();

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var player = new MediaPlayer
                    {
                        Volume = 0,
                        ScrubbingEnabled = true
                    };

                    player.Open(new Uri(videoPath));
                    player.Pause();
                    player.Position = TimeSpan.FromSeconds(1);

                    // Даем немного времени на буферизацию и поиск кадра
                    // MediaPlayer в WPF не имеет удобного события SeekCompleted, поэтому используем задержку
                    await Task.Delay(800);

                    // Рендерим кадр
                    var width = 320;
                    var height = 180;
                    
                    if (player.NaturalVideoWidth > 0)
                    {
                        var aspect = (double)player.NaturalVideoWidth / player.NaturalVideoHeight;
                        width = 320;
                        height = (int)(width / aspect);
                    }

                    var drawingVisual = new DrawingVisual();
                    using (var context = drawingVisual.RenderOpen())
                    {
                        context.DrawVideo(player, new Rect(0, 0, width, height));
                    }

                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(drawingVisual);

                    // Кодируем в JPEG
                    var encoder = new JpegBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    var tempPath = Path.Combine(Path.GetTempPath(), $"memalerts_thumb_{Guid.NewGuid():N}.jpg");
                    
                    using (var fileStream = new FileStream(tempPath, FileMode.Create))
                    {
                        encoder.Save(fileStream);
                    }

                    player.Close();
                    tcs.SetResult(new Uri(tempPath));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return await tcs.Task;
        }
        catch
        {
            // В случае ошибки возвращаем заглушку
            return new Uri("https://dummyimage.com/320x180/333/fff.png&text=No+Preview");
        }
    }
}

