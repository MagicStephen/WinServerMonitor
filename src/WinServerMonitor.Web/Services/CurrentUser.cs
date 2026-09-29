using Microsoft.AspNetCore.Components.Authorization;

namespace WinServerMonitor.Web.Services;

/// <summary>Name of the signed-in user (Windows account when Windows authentication is enabled).</summary>
public sealed class CurrentUser(AuthenticationStateProvider authenticationStateProvider)
{
    public async Task<string> GetNameAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        return state.User.Identity is { IsAuthenticated: true, Name: { } name } ? name : Environment.UserName;
    }
}
