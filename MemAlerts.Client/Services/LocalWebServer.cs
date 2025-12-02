using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MemAlerts.Shared.Models;

namespace MemAlerts.Client.Services;

public class LocalWebServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _url;
    private readonly string _origin;
    private bool _isRunning;

    public string BaseUrl => _url;

    public LocalWebServer(AppConfig config)
    {
        var port = config.LocalWebServerPort > 0 ? config.LocalWebServerPort : 5055;
        _url = $"http://localhost:{port}/"; // Using localhost is often more trusted for WebView origins
        _origin = _url.TrimEnd('/');
        _listener = new HttpListener();
        _listener.Prefixes.Add(_url);
    }

    public void Start()
    {
        if (_isRunning) return;
        
        try 
        {
            _listener.Start();
            _isRunning = true;
            Task.Run(ListenLoop);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Server start error: {ex.Message}");
        }
    }

    private async Task ListenLoop()
    {
        while (_isRunning)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                await ProcessRequest(context);
            }
            catch (HttpListenerException)
            {
                // Listener stopped
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Request error: {ex.Message}");
            }
        }
    }

    private async Task ProcessRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            if (request.Url?.AbsolutePath == "/embed")
            {
                var videoId = request.QueryString["v"];
                var autoplay = request.QueryString["autoplay"] != "0"; // Default to true if not "0"
                if (!string.IsNullOrEmpty(videoId))
                {
                    var html = GetEmbedHtml(videoId, autoplay);
                    var buffer = Encoding.UTF8.GetBytes(html);
                    
                    response.ContentType = "text/html";
                    response.ContentLength64 = buffer.Length;
                    response.StatusCode = 200;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else
                {
                    response.StatusCode = 400;
                }
            }
            else
            {
                response.StatusCode = 404;
            }

            response.Close();
        }
        catch
        {
            // Ignore response errors
        }
    }

    private string GetEmbedHtml(string videoId, bool autoplay)
    {
        var autoplayValue = autoplay ? "1" : "0";
        // The key fix for Error 153 is ensuring the iframe is served from a proper http:// context
        // and NOT using navigateToString (data: uri) which has no origin.
        // We also strictly control the origin parameter.
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <title>Player</title>
    <style>
        body {{ margin: 0; padding: 0; background-color: black; overflow: hidden; }}
        iframe {{ width: 100vw; height: 100vh; border: none; }}
    </style>
    <script src='https://www.youtube.com/iframe_api'></script>
    <script>
        var player;
        function onYouTubeIframeAPIReady() {{
            player = new YT.Player('player', {{
                events: {{
                    'onReady': onPlayerReady
                }}
            }});
        }}
        function onPlayerReady(event) {{
            if ({autoplayValue} === 1) {{
                event.target.playVideo();
            }}
            // Default to 100% volume for alerts
            event.target.setVolume(100);
        }}
        // Exposed function for WebView2 to call
        function setVolume(level) {{
            if (player && player.setVolume) {{
                player.setVolume(level);
            }}
        }}
        function playVideo() {{
            if (player && player.playVideo) {{
                player.playVideo();
            }}
        }}
        function pauseVideo() {{
            if (player && player.pauseVideo) {{
                player.pauseVideo();
            }}
        }}
    </script>
</head>
<body>
    <iframe 
        id='player'
        type='text/html'
        src='https://www.youtube.com/embed/{videoId}?autoplay={autoplayValue}&controls=0&rel=0&modestbranding=1&enablejsapi=1&origin={_origin}'
        frameborder='0'
        allow='accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share' 
        allowfullscreen>
    </iframe>
</body>
</html>";
    }

    public void Dispose()
    {
        _isRunning = false;
        _listener.Stop();
        _listener.Close();
    }
}
