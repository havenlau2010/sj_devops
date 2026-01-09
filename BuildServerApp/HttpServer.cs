using System.Net;
using System.Text;
using System.Text.Json;

namespace BuildServerApp;

public class HttpServer
{
    private HttpListener _listener;
    private BuildManager _buildManager;
    private Action<string> _log;
    private bool _running = false;

    public HttpServer(BuildManager buildManager, Action<string> log)
    {
        _buildManager = buildManager;
        _log = log;
    }

    public void Start(int port)
    {
        if (_running) return;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _running = true;
        _log($"Server started on port {port}");
        Task.Run(ListenLoop);
    }

    public void Stop()
    {
        _running = false;
        _listener?.Stop();
        _log("Server stopped.");
    }

    private async Task ListenLoop()
    {
        while (_running)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                ProcessRequest(ctx);
            }
            catch (HttpListenerException)
            {
                // Listener stopped
            }
            catch (Exception ex)
            {
                _log($"Listener Error: {ex.Message}");
            }
        }
    }

    private async void ProcessRequest(HttpListenerContext ctx)
    {
         try
        {
            if (ctx.Request.Url.AbsolutePath == "/run")
            {
                _log("Received /run request");
                var result = await _buildManager.RunWorkflowAsync();
                
                string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                byte[] buf = Encoding.UTF8.GetBytes(json);
                
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            }
            else
            {
                ctx.Response.StatusCode = 404;
                byte[] buf = Encoding.UTF8.GetBytes("Not Found");
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            }
        }
        catch (Exception ex)
        {
            _log($"Request Handling Error: {ex.Message}");
            ctx.Response.StatusCode = 500;
        }
        finally
        {
            ctx.Response.OutputStream.Close();
        }
    }
}
