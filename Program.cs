using adb_entra_auth_test.Services;
using System.Security.Cryptography.X509Certificates;

// OCI Vault secret OCIDs - set via environment variables
var privateKeySecretId = Environment.GetEnvironmentVariable("OCI_PRIVATE_KEY_SECRET_ID")
    ?? throw new InvalidOperationException("OCI_PRIVATE_KEY_SECRET_ID environment variable is required");
var certificateSecretId = Environment.GetEnvironmentVariable("OCI_CERTIFICATE_SECRET_ID")
    ?? throw new InvalidOperationException("OCI_CERTIFICATE_SECRET_ID environment variable is required");

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
var oracleUserId = Environment.GetEnvironmentVariable("ORACLE_USER_ID")
    ?? throw new InvalidOperationException("ORACLE_USER_ID environment variable is required");
var tnsAdmin = Environment.GetEnvironmentVariable("TNS_ADMIN")
    ?? throw new InvalidOperationException("TNS_ADMIN environment variable is required");
var walletPassword = Environment.GetEnvironmentVariable("ORACLE_WALLET_PASSWORD");

try
{
    // Step 1: Initialize OCI Vault service
    Console.WriteLine("Initializing OCI Vault service...");
    var vaultService = new OciVaultService();

    // Step 2: Retrieve PEM private key and certificate from OCI Vault in parallel
    Console.WriteLine("Retrieving secrets from OCI Vault...");
    var privateKeyTask = vaultService.GetSecretAsync(privateKeySecretId);
    var certificateTask = vaultService.GetSecretAsync(certificateSecretId);

    await Task.WhenAll(privateKeyTask, certificateTask);

    var privateKeyPem = await privateKeyTask;
    var certificatePem = await certificateTask;
    Console.WriteLine("Secrets retrieved successfully from OCI Vault.");

    // Step 3: Load the X509Certificate2 from PEM
    Console.WriteLine("Loading certificate from PEM...");
    var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
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
        userId: oracleUserId,
        accessToken: authResult.AccessToken,
        tnsAdmin: tnsAdmin,
        walletPassword: walletPassword
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
    Console.WriteLine("\nExecuting sample query: SELECT * FROM DUAL");
    var result = await oracleService.ExecuteQueryAsync("SELECT 'Hello from Oracle ADB!' AS MESSAGE FROM DUAL");

    foreach (System.Data.DataRow row in result.Rows)
    {
        Console.WriteLine($"Result: {row["MESSAGE"]}");
    }

    // Run a query with the current timestamp
    Console.WriteLine("\nExecuting timestamp query...");
    var timestamp = await oracleService.ExecuteScalarAsync<DateTime>(
        "SELECT SYSTIMESTAMP FROM DUAL");
    Console.WriteLine($"Database timestamp: {timestamp}");

    Console.WriteLine("\n--- All queries completed successfully ---");
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
