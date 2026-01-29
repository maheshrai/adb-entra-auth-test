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
    /// Creates a new Entra authentication service using a PFX certificate from bytes.
    /// </summary>
    /// <param name="clientId">The application (client) ID from Entra app registration.</param>
    /// <param name="tenantId">The directory (tenant) ID from Entra.</param>
    /// <param name="pfxBytes">The PFX certificate bytes.</param>
    /// <param name="pfxPassword">The password for the PFX file.</param>
    /// <param name="scopes">The scopes to request.</param>
    public EntraAuthService(
        string clientId,
        string tenantId,
        byte[] pfxBytes,
        string pfxPassword,
        string[] scopes)
        : this(clientId, tenantId, LoadCertificate(pfxBytes, pfxPassword), scopes)
    {
    }

    /// <summary>
    /// Creates a new Entra authentication service using a base64-encoded PFX certificate.
    /// </summary>
    /// <param name="clientId">The application (client) ID from Entra app registration.</param>
    /// <param name="tenantId">The directory (tenant) ID from Entra.</param>
    /// <param name="pfxBase64">The PFX certificate as a base64 string.</param>
    /// <param name="pfxPassword">The password for the PFX file.</param>
    /// <param name="scopes">The scopes to request.</param>
    public static EntraAuthService FromBase64Pfx(
        string clientId,
        string tenantId,
        string pfxBase64,
        string pfxPassword,
        string[] scopes)
    {
        var pfxBytes = Convert.FromBase64String(pfxBase64);
        return new EntraAuthService(clientId, tenantId, pfxBytes, pfxPassword, scopes);
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

    /// <summary>
    /// Loads an X509Certificate2 from PFX bytes.
    /// </summary>
    private static X509Certificate2 LoadCertificate(byte[] pfxBytes, string password)
    {
        return X509CertificateLoader.LoadPkcs12(pfxBytes, password, X509KeyStorageFlags.MachineKeySet);
    }
}
