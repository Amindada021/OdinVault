using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace OdinVault.Storage.GoogleDrive;

public sealed record GoogleDriveOAuthOptions(
    string ClientId,
    string ClientSecret,
    string ApplicationName = "OdinVault");

public sealed record GoogleDriveTokenResult(
    string RefreshToken,
    string? AccessToken,
    long? ExpiresInSeconds);

public sealed class GoogleDriveOAuthService(GoogleDriveOAuthOptions options)
{
    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var flow = CreateFlow();
        var request = flow.CreateAuthorizationCodeRequest(redirectUri);
        request.Scope = string.Join(' ', Scopes);
        request.State = state;

        var url = request.Build().AbsoluteUri;
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}access_type=offline&prompt=consent";
    }

    public async Task<GoogleDriveTokenResult> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var flow = CreateFlow();
        var token = await flow.ExchangeCodeForTokenAsync(
            "odinvault-agent",
            code,
            redirectUri,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new InvalidOperationException("Google did not return a refresh token. Reconnect with consent enabled.");

        return new GoogleDriveTokenResult(token.RefreshToken, token.AccessToken, token.ExpiresInSeconds);
    }

    public DriveService CreateDriveService(string refreshToken)
    {
        var flow = CreateFlow();
        var credential = new UserCredential(
            flow,
            "odinvault-agent",
            new TokenResponse { RefreshToken = refreshToken });

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = options.ApplicationName
        });
    }

    private GoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets
        {
            ClientId = options.ClientId,
            ClientSecret = options.ClientSecret
        },
        Scopes = Scopes
    });
}
