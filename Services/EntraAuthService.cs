using Microsoft.Identity.Client;
using System.Security.Cryptography.X509Certificates;

namespace adb_entra_auth_test.Services;

/// <summary>
/// Service for authenticating with Microsoft Entra ID (Azure AD) using MSAL.
/// Retrieves access tokens for Oracle Autonomous Database authentication.
/// </summary>
public class EntraAuthService
{
    private readonly IConfidentialClientApplication _msalClient;
    private readonly string[] _scopes;

    /// <summary>
    /// Creates a new Entra authentication service using an X509 certificate.
    /// </summary>
    /// <param name="clientId">The application (client) ID from Entra app registration.</param>
    /// <param name="tenantId">The directory (tenant) ID from Entra.</param>
    /// <param name="certificate">The X509 certificate for authentication.</param>
    /// <param name="scopes">The scopes to request. For Oracle ADB, typically the application ID URI.</param>
    public EntraAuthService(string clientId, string tenantId, X509Certificate2 certificate, string[] scopes)
    {
        _scopes = scopes;
        _msalClient = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithCertificate(certificate)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .Build();
    }

    /// <summary>
    /// Creates a new Entra authentication service using a PEM certificate and encrypted private key.
    /// </summary>
    /// <param name="clientId">The application (client) ID from Entra app registration.</param>
    /// <param name="tenantId">The directory (tenant) ID from Entra.</param>
    /// <param name="certificatePem">The certificate in PEM format.</param>
    /// <param name="privateKeyPem">The encrypted private key in PEM format.</param>
    /// <param name="privateKeyPassword">The password for the encrypted private key.</param>
    /// <param name="scopes">The scopes to request.</param>
    public EntraAuthService(
        string clientId,
        string tenantId,
        string certificatePem,
        string privateKeyPem,
        string privateKeyPassword,
        string[] scopes)
        : this(clientId, tenantId, X509Certificate2.CreateFromEncryptedPem(certificatePem, privateKeyPem, privateKeyPassword), scopes)
    {
    }

    /// <summary>
    /// Gets an access token from Entra ID for Oracle ADB authentication.
    /// </summary>
    /// <returns>The access token string.</returns>
    public async Task<string> GetAccessTokenAsync()
    {
        var result = await _msalClient
            .AcquireTokenForClient(_scopes)
            .ExecuteAsync();

        return result.AccessToken;
    }

    /// <summary>
    /// Gets the full authentication result including token, expiration, etc.
    /// </summary>
    /// <returns>The authentication result from MSAL.</returns>
    public async Task<AuthenticationResult> GetAuthenticationResultAsync()
    {
        return await _msalClient
            .AcquireTokenForClient(_scopes)
            .ExecuteAsync();
    }
}
