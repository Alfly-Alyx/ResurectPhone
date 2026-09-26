using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Renci.SshNet;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

// Temporary, loopback-only HTTP proxy carried over the pinned SSH connection.
// Only the published Harmattan mirrors are reachable; upstream TLS is checked by Windows.
internal sealed class N9RepositoryRelay : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(2) };
    private readonly List<Task> _requests = [];
    private readonly SshClient _ssh;
    private readonly ForwardedPortRemote _forward;
    private readonly Task _accept;
    public string AptOptions => $" -o Acquire::http::Proxy=http://127.0.0.1:{_forward.BoundPort}" +
        " -o Acquire::http::Pipeline-Depth=0 -o Acquire::Retries=0 -o Acquire::http::Timeout=60";

    public N9RepositoryRelay(SshClient ssh)
    {
        _ssh = ssh;
        _listener.Start();
        // OpenSSH 5.1 on Harmattan predates reliable server-allocated port replies.
        _forward = new ForwardedPortRemote("127.0.0.1", (uint)Random.Shared.Next(30000, 55000), "127.0.0.1", (uint)((IPEndPoint)_listener.LocalEndpoint).Port);
        try
        {
            ssh.AddForwardedPort(_forward);
            _forward.Start();
            _accept = AcceptAsync();
        }
        catch
        {
            _listener.Stop();
            _forward.Dispose();
            _http.Dispose();
            _stop.Dispose();
            throw;
        }
    }

    internal static Uri? ResolveUpstream(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            uri.Host != "wunderwungiel.pl" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath.Contains('%')) return null;
        string[] roots = ["/MeeGo/n9mirror/", "/MeeGo/harmattan-dev.nokia.com/"];
        if (!roots.Any(root => uri.AbsolutePath.StartsWith(root, StringComparison.Ordinal))) return null;
        return new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                _requests.RemoveAll(task => task.IsCompleted);
                _requests.Add(ServeAsync(socket));
            }
        }
        catch (Exception e) when (_stop.IsCancellationRequested && e is OperationCanceledException or SocketException) { }
    }

    private async Task ServeAsync(TcpClient socket)
    {
        using (socket)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            var token = deadline.Token;
            using var stream = socket.GetStream();
            try
            {
                // Bounded headers, without StreamReader's unbounded ReadLine allocation.
                var header = new List<byte>();
                var buffer = new byte[1];
                while (header.Count < 16384)
                {
                    if (await stream.ReadAsync(buffer, token) == 0) return;
                    header.Add(buffer[0]);
                    if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
                }
                var first = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n")[0].Split(' ');
                var upstream = first.Length == 3 && first[0] == "GET" ? ResolveUpstream(first[1]) : null;
                if (upstream is null || header.Count >= 16384)
                {
                    await ReplyAsync(stream, 403, "Forbidden", [], token);
                    return;
                }
                using var response = await _http.GetAsync(upstream, HttpCompletionOption.ResponseHeadersRead, token);
                // Reject redirects so a mirror cannot turn this into a general-purpose proxy.
                if (!response.IsSuccessStatusCode)
                {
                    await ReplyAsync(stream, (int)response.StatusCode, "Upstream response", [], token);
                    return;
                }
                var length = response.Content.Headers.ContentLength;
                if (length is null or > 256 * 1024 * 1024)
                {
                    await ReplyAsync(stream, 502, "Invalid upstream length", [], token);
                    return;
                }
                var prefix = Encoding.ASCII.GetBytes($"HTTP/1.0 200 OK\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(prefix, token);
                await response.Content.CopyToAsync(stream, token);
            }
            catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException or SocketException)
            {
                // Closing the connection is an APT download failure, never a successful partial index.
            }
        }
    }

    private static async Task ReplyAsync(Stream stream, int code, string reason, byte[] body, CancellationToken token)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.0 {code} {reason}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), token);
        await stream.WriteAsync(body, token);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { _forward.Stop(); } catch (Exception e) when (e is Renci.SshNet.Common.SshException or SocketException) { }
        _ssh.RemoveForwardedPort(_forward);
        _forward.Dispose();
        await _accept;
        await Task.WhenAll(_requests);
        _http.Dispose();
        _stop.Dispose();
    }
}
