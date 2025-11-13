namespace TokenRelay.Models;

public class OAuthToken
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; } = 3600; // seconds
    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
    public string? RefreshToken { get; set; }
    public string? Scope { get; set; }

    // Calculated property
    public DateTime ExpiresAt => AcquiredAt.AddSeconds(ExpiresIn);

    // Check if token is expired (with 60 second buffer)
    public bool IsExpired(int bufferSeconds = 60)
        => DateTime.UtcNow >= ExpiresAt.AddSeconds(-bufferSeconds);

    // Get formatted authorization header value
    public string GetAuthorizationHeaderValue()
        => $"{TokenType} {AccessToken}";
}
