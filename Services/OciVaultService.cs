using Oci.Common;
using Oci.Common.Auth;
using Oci.SecretsService;
using Oci.SecretsService.Requests;
using System.Security;
using System.Text;

namespace adb_entra_auth_test.Services;

/// <summary>
/// Service for retrieving secrets from OCI Vault.
/// </summary>
public class OciVaultService
{
    private readonly SecretsClient _secretsClient;

    /// <summary>
    /// Creates a new OCI Vault service using the specified authentication provider.
    /// </summary>
    /// <param name="authProvider">The OCI authentication provider to use.</param>
    public OciVaultService(IBasicAuthenticationDetailsProvider authProvider)
    {
        _secretsClient = new SecretsClient(authProvider);
    }

    /// <summary>
    /// Creates a new OCI Vault service using the default config file authentication.
    /// Looks for config at ~/.oci/config with the DEFAULT profile.
    /// </summary>
    public OciVaultService()
        : this(new ConfigFileAuthenticationDetailsProvider("DEFAULT"))
    {
    }

    /// <summary>
    /// Creates a new OCI Vault service using config file authentication with a specific profile.
    /// </summary>
    /// <param name="profile">The OCI config profile name to use.</param>
    public OciVaultService(string profile)
        : this(new ConfigFileAuthenticationDetailsProvider(profile))
    {
    }

    /// <summary>
    /// Creates a new OCI Vault service using instance principal authentication.
    /// Use this when running on OCI compute instances.
    /// </summary>
    /// <returns>A new OciVaultService configured for instance principal auth.</returns>
    public static OciVaultService CreateWithInstancePrincipal()
    {
        var provider = new InstancePrincipalsAuthenticationDetailsProvider();
        return new OciVaultService(provider);
    }

    /// <summary>
    /// Creates a new OCI Vault service using environment variables for authentication.
    /// Required: OCI_TENANCY_OCID, OCI_USER_OCID, OCI_FINGERPRINT, OCI_REGION, OCI_KEY_FILE.
    /// Optional: OCI_KEY_PASSPHRASE.
    /// </summary>
    /// <returns>A new OciVaultService configured from environment variables.</returns>
    public static OciVaultService CreateFromEnvironment()
    {
        var tenancyId = Environment.GetEnvironmentVariable("OCI_TENANCY_OCID")
            ?? throw new InvalidOperationException("OCI_TENANCY_OCID environment variable is required");
        var userId = Environment.GetEnvironmentVariable("OCI_USER_OCID")
            ?? throw new InvalidOperationException("OCI_USER_OCID environment variable is required");
        var fingerprint = Environment.GetEnvironmentVariable("OCI_FINGERPRINT")
            ?? throw new InvalidOperationException("OCI_FINGERPRINT environment variable is required");
        var region = Environment.GetEnvironmentVariable("OCI_REGION")
            ?? throw new InvalidOperationException("OCI_REGION environment variable is required");
        var keyFile = Environment.GetEnvironmentVariable("OCI_KEY_FILE")
            ?? throw new InvalidOperationException("OCI_KEY_FILE environment variable is required");
        var passphrase = Environment.GetEnvironmentVariable("OCI_KEY_PASSPHRASE");

        var securePassphrase = new SecureString();
        if (!string.IsNullOrEmpty(passphrase))
        {
            foreach (var c in passphrase)
                securePassphrase.AppendChar(c);
        }
        securePassphrase.MakeReadOnly();

        var keySupplier = new FilePrivateKeySupplier(keyFile, securePassphrase);

        var provider = new SimpleAuthenticationDetailsProvider
        {
            TenantId = tenancyId,
            UserId = userId,
            Fingerprint = fingerprint,
            Region = Region.FromRegionId(region),
            PrivateKeySupplier = keySupplier
        };

        return new OciVaultService(provider);
    }

    /// <summary>
    /// Retrieves a secret value from OCI Vault by its OCID.
    /// </summary>
    /// <param name="secretId">The OCID of the secret to retrieve.</param>
    /// <returns>The secret value as a string.</returns>
    public async Task<string> GetSecretAsync(string secretId)
    {
        var request = new GetSecretBundleRequest
        {
            SecretId = secretId,
            Stage = GetSecretBundleRequest.StageEnum.Current
        };

        var response = await _secretsClient.GetSecretBundle(request);
        var secretContent = response.SecretBundle.SecretBundleContent;

        if (secretContent is Oci.SecretsService.Models.Base64SecretBundleContentDetails base64Content)
        {
            var decodedBytes = Convert.FromBase64String(base64Content.Content);
            return Encoding.UTF8.GetString(decodedBytes);
        }

        throw new InvalidOperationException($"Unexpected secret content type: {secretContent.GetType().Name}");
    }

    /// <summary>
    /// Retrieves a secret value from OCI Vault as raw bytes.
    /// Use this for binary content (e.g., DER-encoded keys) that would be corrupted by UTF-8 conversion.
    /// </summary>
    /// <param name="secretId">The OCID of the secret to retrieve.</param>
    /// <returns>The secret value as a byte array.</returns>
    public async Task<byte[]> GetSecretBytesAsync(string secretId)
    {
        var request = new GetSecretBundleRequest
        {
            SecretId = secretId,
            Stage = GetSecretBundleRequest.StageEnum.Current
        };

        var response = await _secretsClient.GetSecretBundle(request);
        var secretContent = response.SecretBundle.SecretBundleContent;

        if (secretContent is Oci.SecretsService.Models.Base64SecretBundleContentDetails base64Content)
        {
            return Convert.FromBase64String(base64Content.Content);
        }

        throw new InvalidOperationException($"Unexpected secret content type: {secretContent.GetType().Name}");
    }

    /// <summary>
    /// Retrieves a private key from OCI Vault.
    /// </summary>
    /// <param name="secretId">The OCID of the secret containing the private key.</param>
    /// <returns>The private key as a string (PEM format).</returns>
    public async Task<string> GetPrivateKeyAsync(string secretId)
    {
        return await GetSecretAsync(secretId);
    }

    /// <summary>
    /// Retrieves a password from OCI Vault.
    /// </summary>
    /// <param name="secretId">The OCID of the secret containing the password.</param>
    /// <returns>The password as a string.</returns>
    public async Task<string> GetPasswordAsync(string secretId)
    {
        return await GetSecretAsync(secretId);
    }

    /// <summary>
    /// Retrieves multiple secrets from OCI Vault.
    /// </summary>
    /// <param name="secretIds">Dictionary mapping secret names to their OCIDs.</param>
    /// <returns>Dictionary mapping secret names to their values.</returns>
    public async Task<Dictionary<string, string>> GetSecretsAsync(Dictionary<string, string> secretIds)
    {
        var tasks = secretIds.Select(async kvp =>
        {
            var value = await GetSecretAsync(kvp.Value);
            return new KeyValuePair<string, string>(kvp.Key, value);
        });

        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
