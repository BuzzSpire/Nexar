using System.Text;
using System.Text.Json;
using Nexar.Http.Abstract;

namespace Nexar;

public class Nexar : INexar, IDisposable
{
    private readonly HttpClient _client = new HttpClient();

    public async Task<string> GetAsync(string url, Dictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var header in headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> PostAsync<T>(string url, Dictionary<string, string> headers, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body))
        };

        foreach (var header in headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> PutAsync<T>(string url, Dictionary<string, string> headers, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url);

        foreach (var header in headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> DeleteAsync(string url, Dictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        foreach (var header in headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> PatchAsync<T>(string url, Dictionary<string, string> headers, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url);

        foreach (var header in headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> HeadAsync(string url, Dictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Head, url);
        foreach (var header in headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
