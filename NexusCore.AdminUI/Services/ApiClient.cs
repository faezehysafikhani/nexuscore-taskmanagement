using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NexusCore.AdminUI.Services;

public sealed class ApiClient(HttpClient httpClient, AuthTokenStore tokenStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ApiResult<T>> GetAsync<T>(string url) => SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, url));

    public Task<ApiResult<T>> PostAsync<T>(string url, object body) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) });

    public Task<ApiResult<T>> PutAsync<T>(string url, object body) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) });

    public async Task<ApiResult> PutAsync(string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        await AddBearerAsync(request);
        using var response = await httpClient.SendAsync(request);
        return response.IsSuccessStatusCode
            ? new ApiResult(true, null)
            : new ApiResult(false, await response.Content.ReadAsStringAsync());
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request)
    {
        using (request)
        {
            await AddBearerAsync(request);
            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return new ApiResult<T>(false, default, await response.Content.ReadAsStringAsync());
            }

            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
            return new ApiResult<T>(true, value, null);
        }
    }

    private async Task AddBearerAsync(HttpRequestMessage request)
    {
        var token = await tokenStore.GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}

public sealed record ApiResult(bool IsSuccess, string? Error);
public sealed record ApiResult<T>(bool IsSuccess, T? Value, string? Error);
