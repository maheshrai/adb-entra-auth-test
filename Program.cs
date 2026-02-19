using adb_entra_auth_test.Services;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

// Load .env file if present
DotNetEnv.Env.Load();

// Secrets - set via environment variables
var privateKeySecretId = Environment.GetEnvironmentVariable("APP_ENTRA_TEST_PRIVATE_KEY")
    ?? throw new InvalidOperationException("APP_ENTRA_TEST_PRIVATE_KEY environment variable is required");
var privateKeyPasswordSecretId = Environment.GetEnvironmentVariable("APP_ENTRA_TEST_PRIVATE_KEY_PWD")
    ?? throw new InvalidOperationException("APP_ENTRA_TEST_PRIVATE_KEY_PWD environment variable is required");
var certificateFile = Environment.GetEnvironmentVariable("CERTIFICATE_FILE")
    ?? throw new InvalidOperationException("CERTIFICATE_FILE environment variable is required");

// Entra ID (Azure AD) configuration
var entraClientId = Environment.GetEnvironmentVariable("ENTRA_CLIENT_ID")
    ?? throw new InvalidOperationException("ENTRA_CLIENT_ID environment variable is required");
var entraTenantId = Environment.GetEnvironmentVariable("ENTRA_TENANT_ID")
    ?? throw new InvalidOperationException("ENTRA_TENANT_ID environment variable is required");
var entraScope = Environment.GetEnvironmentVariable("ENTRA_SCOPE")
    ?? $"api://{entraClientId}/.default";

// Oracle ADB configuration
var oracleTnsName = Environment.GetEnvironmentVariable("ORACLE_TNS_NAME")
    ?? throw new InvalidOperationException("ORACLE_TNS_NAME environment variable is required");
var tnsAdmin = Environment.GetEnvironmentVariable("TNS_ADMIN")
    ?? throw new InvalidOperationException("TNS_ADMIN environment variable is required");
var walletLocation = Environment.GetEnvironmentVariable("WALLET_LOCATION")
    ?? throw new InvalidOperationException("WALLET_LOCATION environment variable is required");

try
{
    // Step 1: Initialize OCI Vault service
    Console.WriteLine("Initializing OCI Vault service...");
    var vaultService = OciVaultService.CreateFromEnvironment();

    // Step 2: Read certificate from file and retrieve secrets from OCI Vault in parallel
    Console.WriteLine("Reading certificate from file...");
    Console.WriteLine("Retrieving secrets from OCI Vault...");
    var certificateTask = File.ReadAllTextAsync(certificateFile);
    var privateKeyBytesTask = vaultService.GetSecretBytesAsync(privateKeySecretId);
    var privateKeyPasswordTask = vaultService.GetSecretAsync(privateKeyPasswordSecretId);

    await Task.WhenAll(certificateTask, privateKeyBytesTask, privateKeyPasswordTask);

    var certificateFileContent = await certificateTask;
    var privateKeyBytes = await privateKeyBytesTask;
    var privateKeyPassword = await privateKeyPasswordTask;
    Console.WriteLine("Certificate loaded from file and secrets retrieved from OCI Vault.");

    // Extract PEM block from certificate file (may contain extra text like subject= headers)
    var certPem = certificateFileContent;
    var beginMarker = "-----BEGIN CERTIFICATE-----";
    var endMarker = "-----END CERTIFICATE-----";
    var beginIdx = certificateFileContent.IndexOf(beginMarker);
    if (beginIdx >= 0)
    {
        var endIdx = certificateFileContent.IndexOf(endMarker, beginIdx);
        if (endIdx >= 0)
            certPem = certificateFileContent[beginIdx..(endIdx + endMarker.Length)];
    }

    // Step 3: Load the X509Certificate2 from certificate and private key
    Console.WriteLine("Loading certificate...");
    X509Certificate2 certificate;
    var privateKeyText = Encoding.UTF8.GetString(privateKeyBytes);
    if (privateKeyText.StartsWith("-----BEGIN"))
    {
        // PEM-encoded private key
        if (privateKeyText.Contains("ENCRYPTED"))
            certificate = X509Certificate2.CreateFromEncryptedPem(certPem, privateKeyText, privateKeyPassword);
        else
            certificate = X509Certificate2.CreateFromPem(certPem, privateKeyText);
    }
    else
    {
        // DER-encoded private key
        var cert = X509Certificate2.CreateFromPem(certPem);
        var rsa = RSA.Create();
        try
        {
            rsa.ImportEncryptedPkcs8PrivateKey(privateKeyPassword.AsSpan(), privateKeyBytes, out _);
        }
        catch (CryptographicException)
        {
            // If encrypted import fails, try unencrypted
            rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
        }
        certificate = cert.CopyWithPrivateKey(rsa);
    }
    Console.WriteLine($"Certificate loaded. Subject: {certificate.Subject}");

    // Step 4: Acquire access token from Entra ID
    Console.WriteLine("Initializing Entra authentication service...");
    var entraService = new EntraAuthService(
        clientId: entraClientId,
        tenantId: entraTenantId,
        certificate: certificate,
        scopes: [entraScope]
    );

    Console.WriteLine("Acquiring access token from Entra ID...");
    var authResult = await entraService.GetAuthenticationResultAsync();
    Console.WriteLine($"Access token acquired. Expires at: {authResult.ExpiresOn}");

    // Step 5: Connect to Oracle ADB using TNS name with the access token
    Console.WriteLine("\nConnecting to Oracle Autonomous Database...");
    Console.WriteLine($"TNS Name: {oracleTnsName}");
    Console.WriteLine($"TNS Admin: {tnsAdmin}");

    await using var oracleService = new OracleDbService(
        tnsName: oracleTnsName,
        accessToken: authResult.AccessToken,
        tnsAdmin: tnsAdmin,
        walletLocation: walletLocation
    );

    await oracleService.OpenAsync();
    Console.WriteLine("Connected to Oracle ADB successfully.");

    // Step 6: Run test queries
    Console.WriteLine("\n--- Running Test Queries ---\n");

    // Get current user
    var currentUser = await oracleService.GetCurrentUserAsync();
    Console.WriteLine($"Current database user: {currentUser}");

    // Get database version
    var dbVersion = await oracleService.GetDatabaseVersionAsync();
    Console.WriteLine($"Database version: {dbVersion}");

    // Run a sample query
    Console.WriteLine("\nExecuting query: SELECT * FROM app_test");
    var result = await oracleService.ExecuteQueryAsync("SELECT * FROM app_test");

    foreach (System.Data.DataRow row in result.Rows)
    {
        foreach (System.Data.DataColumn col in result.Columns)
        {
            Console.WriteLine($"  {col.ColumnName}: {row[col]}");
        }
        Console.WriteLine();
    }

    Console.WriteLine("--- All queries completed successfully ---");
}
catch (Exception ex)
{
    Console.WriteLine($"\nError: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner error: {ex.InnerException.Message}");
    }
    Environment.Exit(1);
}
