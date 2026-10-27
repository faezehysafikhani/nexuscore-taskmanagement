using Microsoft.JSInterop;

namespace NexusCore.AdminUI.Services;

public sealed class AuthTokenStore(IJSRuntime jsRuntime)
{
    private const string AccessTokenKey = "nexuscore.access_token";
    // Older versions kept the refresh token here; it now lives only in an HttpOnly cookie.
    private const string RefreshTokenKey = "nexuscore.refresh_token";

    public async Task SetAsync(string accessToken)
    {
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", AccessTokenKey, accessToken);
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
    }

    public async Task<string?> GetAccessTokenAsync() =>
        await jsRuntime.InvokeAsync<string?>("localStorage.getItem", AccessTokenKey);

    public async Task ClearAsync()
    {
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", AccessTokenKey);
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
    }
}
