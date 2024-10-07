using Nexar.Http.Abstract;


namespace Nexar
{
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

        public async Task<string> PostAsync(string url, Dictionary<string, string> headers, string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body)
            };

            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> PutAsync(string url, Dictionary<string, string> headers, string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(body)
            };

            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

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

        public async Task<string> PatchAsync(string url, Dictionary<string, string> headers, string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = new StringContent(body)
            };

            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

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
        }
        
        ~Nexar()
        {
            Dispose();
        }
    }
}
