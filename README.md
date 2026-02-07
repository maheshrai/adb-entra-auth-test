# Oracle ADB with Entra ID Token Authentication

A .NET application that connects to Oracle Autonomous Database (ADB) using Microsoft Entra ID (Azure AD) token-based authentication. Secrets are securely retrieved from OCI Vault.

## Architecture

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  OCI Vault  │────▶│   Entra ID  │────▶│ Access Token│────▶│ Oracle ADB  │
│ (PEM Cert+  │     │   (MSAL)    │     │   (OAuth)   │     │  (TNS Name) │
│  Priv Key)  │     │             │     │             │     │             │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
```

## Prerequisites

- .NET 10.0 SDK
- OCI CLI configured (`~/.oci/config`)
- Oracle ADB wallet downloaded
- Entra ID app registration with certificate authentication

## Configuration

### Environment Variables

```bash
# Secrets
export APP_ENTRA_TEST_PRIVATE_KEY="ocid1.vaultsecret.oc1..xxxxx"       # Encrypted PEM private key
export APP_ENTRA_TEST_PRIVATE_KEY_PWD="ocid1.vaultsecret.oc1..xxxxx"   # Private key password
export APP_ENTRA_TEST_CERTIFICATE="ocid1.vaultsecret.oc1..xxxxx"       # PEM certificate

# Entra ID (Azure AD)
export ENTRA_CLIENT_ID="your-app-client-id"
export ENTRA_TENANT_ID="your-tenant-id"
export ENTRA_SCOPE="api://your-client-id/.default"            # Optional

# Oracle ADB
export ORACLE_TNS_NAME="mydb_high"                            # TNS alias from tnsnames.ora
export ORACLE_USER_ID="your_db_user"
export TNS_ADMIN="/path/to/wallet"                            # Wallet directory
export ORACLE_WALLET_PASSWORD="wallet_password"               # Optional
```

### Entra ID Setup

1. Register an application in Entra ID
2. Upload the certificate (public key) to the app registration
3. Configure API permissions as needed
4. Note the Client ID and Tenant ID

### Oracle ADB Setup

1. Download the wallet from OCI Console
2. Extract to a directory (this becomes TNS_ADMIN)
3. The directory should contain:
   - `tnsnames.ora`
   - `sqlnet.ora`
   - `cwallet.sso` (auto-open wallet)

## Usage

### Build and Run

```bash
dotnet build
dotnet run
```

### Expected Output

```
Initializing OCI Vault service...
Retrieving secrets from OCI Vault...
Secrets retrieved successfully from OCI Vault.
Loading certificate from PEM...
Certificate loaded. Subject: CN=your-app
Initializing Entra authentication service...
Acquiring access token from Entra ID...
Access token acquired. Expires at: 1/29/2026 11:00:00 PM +00:00

Connecting to Oracle Autonomous Database...
TNS Name: mydb_high
TNS Admin: /path/to/wallet
Connected to Oracle ADB successfully.

--- Running Test Queries ---

Current database user: YOUR_USER
Database version: Oracle Database 19c Enterprise Edition...

Executing sample query: SELECT * FROM DUAL
Result: Hello from Oracle ADB!

Executing timestamp query...
Database timestamp: 1/29/2026 10:00:00 PM

--- All queries completed successfully ---
```

## Project Structure

```
├── Program.cs                      # Main entry point
├── Services/
│   ├── OciVaultService.cs          # OCI Vault secret retrieval
│   ├── EntraAuthService.cs         # Entra ID authentication (MSAL)
│   └── OracleDbService.cs          # Oracle ADB connection and queries
├── adb-entra-auth-test.csproj      # Project file
└── .gitignore
```

## Services

### OciVaultService

Retrieves secrets from OCI Vault using the OCI .NET SDK.

```csharp
var vaultService = new OciVaultService();  // Uses ~/.oci/config
var secret = await vaultService.GetSecretAsync(secretId);
```

### EntraAuthService

Authenticates with Entra ID using certificate-based authentication via MSAL.

```csharp
var entraService = new EntraAuthService(clientId, tenantId, certificate, scopes);
var token = await entraService.GetAccessTokenAsync();
```

### OracleDbService

Connects to Oracle ADB using TNS name and OAuth token authentication.

```csharp
var oracleService = new OracleDbService(tnsName, userId, accessToken, tnsAdmin);
await oracleService.OpenAsync();
var result = await oracleService.ExecuteQueryAsync("SELECT * FROM my_table");
```

## Dependencies

- `Microsoft.Identity.Client` - MSAL for Entra ID authentication
- `OCI.DotNetSDK.Secrets` - OCI Vault access
- `OCI.DotNetSDK.Vault` - OCI Vault management
- `Oracle.ManagedDataAccess.Core` - Oracle database connectivity

## License

MIT
