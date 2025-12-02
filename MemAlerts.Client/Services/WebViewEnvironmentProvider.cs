using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MemAlerts.Client.Services;

/// <summary>
/// Предоставляет единый экземпляр WebView2 окружения, чтобы несколько окон
/// могли использовать одну user-data директорию без конфликтов.
/// </summary>
public sealed class WebViewEnvironmentProvider
{
    private readonly Lazy<Task<CoreWebView2Environment>> _environmentFactory;

    public WebViewEnvironmentProvider()
    {
        _environmentFactory = new Lazy<Task<CoreWebView2Environment>>(CreateEnvironmentAsync);
    }

    public Task<CoreWebView2Environment> GetAsync() => _environmentFactory.Value;

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userDataFolder = Path.Combine(appData, "MemAlerts", "WebView2");
        Directory.CreateDirectory(userDataFolder);

        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        return CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
    }
}
