namespace SpotLyrics.App.Services.Spotify;

public interface ISpotifyAuthService
{
    bool IsAuthenticated { get; }
    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);
    Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default);
    Task LoginInteractiveAsync(CancellationToken cancellationToken = default);
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    void Logout();
}
