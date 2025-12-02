namespace MemAlerts.Shared.Models;

public sealed class AppConfig
{
    public string ServerIp { get; set; } = "127.0.0.1";
    public int ServerPort { get; set; } = 5050;
    public string WebViewUserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    public string YoutubeAndroidUserAgent { get; set; } = "Mozilla/5.0 (Linux; Android 11; Pixel 5 Build/RQ3A.210805.001.A1; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/120.0.6099.230 Mobile Safari/537.36";
    public int LocalWebServerPort { get; set; } = 5055;
}

