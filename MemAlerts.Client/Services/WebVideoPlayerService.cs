using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Wpf;
using MemAlerts.Shared.Models;

namespace MemAlerts.Client.Services;

/// <summary>
/// Общие операции для управления WebView2 с встраиваемыми видеоплеерами.
/// </summary>
public sealed class WebVideoPlayerService
{
    private readonly WebViewEnvironmentProvider _environmentProvider;
    private readonly AppConfig _config;
    private readonly LocalWebServer _localWebServer;

    public WebVideoPlayerService(
        WebViewEnvironmentProvider environmentProvider,
        AppConfig config,
        LocalWebServer localWebServer)
    {
        _environmentProvider = environmentProvider;
        _config = config;
        _localWebServer = localWebServer;
    }

    public async Task EnsureWebViewInitializedAsync(WebView2 webView, string? userAgent = null)
    {
        if (webView.CoreWebView2 != null)
        {
            return;
        }

        var env = await _environmentProvider.GetAsync();
        await webView.EnsureCoreWebView2Async(env);

        var ua = userAgent ?? _config.WebViewUserAgent;

        if (!string.IsNullOrWhiteSpace(ua))
        {
            webView.CoreWebView2.Settings.UserAgent = ua;
        }
    }

    public async Task LoadVideoAsync(WebView2 webView, Uri source, bool autoplay = false)
    {
        await EnsureWebViewInitializedAsync(webView);
        var embedUri = VideoUrlHelper.GetEmbedUri(source, autoplay, _localWebServer.BaseUrl);
        if (webView.Source != embedUri)
        {
            webView.Source = embedUri;
        }
    }

    public async Task PlayAsync(WebView2 webView)
    {
        if (webView?.CoreWebView2 == null) return;
        try
        {
            await webView.CoreWebView2.ExecuteScriptAsync("playVideo();");
        }
        catch { }
    }

    public async Task PauseAsync(WebView2 webView)
    {
        if (webView?.CoreWebView2 == null) return;
        try
        {
            await webView.CoreWebView2.ExecuteScriptAsync("pauseVideo();");
        }
        catch { }
    }

    public async Task SetVolumeAsync(WebView2 webView, int volume)
    {
        if (webView?.CoreWebView2 == null) return;
        try
        {
            await webView.CoreWebView2.ExecuteScriptAsync($"setVolume({volume});");
        }
        catch { }
    }
}
