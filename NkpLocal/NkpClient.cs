using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

public class NkpClient
{
    private readonly HttpClient _http;

    public NkpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.All
        };

        _http = new HttpClient(handler);

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 Chrome/142.0 Safari/537.36");
    }

    public async Task<string> DownloadAsync(string url)
    {
        return await _http.GetStringAsync(url);
    }
}