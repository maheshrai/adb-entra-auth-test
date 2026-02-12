using adb_entra_auth_test.Services;
using System.Security.Cryptography.X509Certificates;

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
var oracleUserId = Environment.GetEnvironmentVariable("ORACLE_USER_ID")
    ?? throw new InvalidOperationException("ORACLE_USER_ID environment variable is required");
var tnsAdmin = Environment.GetEnvironmentVariable("TNS_ADMIN")
    ?? throw new InvalidOperationException("TNS_ADMIN environment variable is required");
var walletPassword = Environment.GetEnvironmentVariable("ORACLE_WALLET_PASSWORD");

try
{
    // Step 1: Initialize OCI Vault service
    Console.WriteLine("Initializing OCI Vault service...");
    var vaultService = OciVaultService.CreateFromEnvironment();

    // Step 2: Read certificate from file and retrieve secrets from OCI Vault in parallel
    Console.WriteLine("Reading certificate from file...");
    Console.WriteLine("Retrieving secrets from OCI Vault...");
    var certificateTask = File.ReadAllTextAsync(certificateFile);
    var privateKeyTask = vaultService.GetSecretAsync(privateKeySecretId);
    var privateKeyPasswordTask = vaultService.GetSecretAsync(privateKeyPasswordSecretId);

    await Task.WhenAll(certificateTask, privateKeyTask, privateKeyPasswordTask);

    var certificatePem = await certificateTask;
    var privateKeyPem = await privateKeyTask;
    var privateKeyPassword = await privateKeyPasswordTask;
    Console.WriteLine("Certificate loaded from file and secrets retrieved from OCI Vault.");

    // Step 3: Load the X509Certificate2 from PEM certificate and encrypted private key
    Console.WriteLine("Loading certificate from PEM...");
    var certificate = X509Certificate2.CreateFromEncryptedPem(certificatePem, privateKeyPem, privateKeyPassword);
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
