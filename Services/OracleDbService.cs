using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace adb_entra_auth_test.Services;

/// <summary>
/// Service for connecting to Oracle Autonomous Database using Entra ID token authentication.
/// </summary>
public class OracleDbService : IDisposable, IAsyncDisposable
{
    private readonly string _connectionString;
    private OracleConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Creates a new Oracle database service using TNS name with token-based authentication.
    /// </summary>
    /// <param name="tnsName">The TNS alias name from tnsnames.ora.</param>
    /// <param name="userId">The database user ID (mapped to Entra identity).</param>
    /// <param name="accessToken">The Entra ID access token.</param>
    /// <param name="tnsAdmin">The TNS_ADMIN directory path containing tnsnames.ora and wallet.</param>
    /// <param name="walletLocation">Optional separate wallet location. If not specified, uses tnsAdmin.</param>
    /// <param name="walletPassword">Optional wallet password (for auto-open wallets, not needed).</param>
    public OracleDbService(
        string tnsName,
        string userId,
        string accessToken,
        string tnsAdmin,
        string? walletLocation = null,
        string? walletPassword = null)
    {
        // Set TNS_ADMIN for Oracle client to find tnsnames.ora
        OracleConfiguration.TnsAdmin = tnsAdmin;

        // Configure wallet location (defaults to TNS_ADMIN if not specified)
        OracleConfiguration.WalletLocation = walletLocation ?? tnsAdmin;

        var builder = new OracleConnectionStringBuilder
        {
            DataSource = tnsName,
            UserID = userId,
        };

        // Set the access token for OAuth/token-based authentication
        builder["Token Authentication"] = "OAUTH";
        builder["Access Token"] = accessToken;

        // Set wallet password if provided
        if (!string.IsNullOrEmpty(walletPassword))
        {
            builder["Wallet Password"] = walletPassword;
        }

        _connectionString = builder.ConnectionString;
    }

    /// <summary>
    /// Creates a new Oracle database service using TNS name from environment variables.
    /// Reads TNS_ADMIN from environment if not specified.
    /// </summary>
    /// <param name="tnsName">The TNS alias name from tnsnames.ora.</param>
    /// <param name="userId">The database user ID (mapped to Entra identity).</param>
    /// <param name="accessToken">The Entra ID access token.</param>
    public OracleDbService(string tnsName, string userId, string accessToken)
        : this(
            tnsName,
            userId,
            accessToken,
            Environment.GetEnvironmentVariable("TNS_ADMIN")
                ?? throw new InvalidOperationException("TNS_ADMIN environment variable is required"))
    {
    }

    /// <summary>
    /// Creates a new Oracle database service with a pre-built connection string and token.
    /// </summary>
    /// <param name="baseConnectionString">The base connection string (without token).</param>
    /// <param name="accessToken">The Entra ID access token.</param>
    /// <param name="tnsAdmin">Optional TNS_ADMIN directory path.</param>
    /// <returns>A new OracleDbService instance.</returns>
    public static OracleDbService FromConnectionString(string baseConnectionString, string accessToken, string? tnsAdmin = null)
    {
        if (!string.IsNullOrEmpty(tnsAdmin))
        {
            OracleConfiguration.TnsAdmin = tnsAdmin;
            OracleConfiguration.WalletLocation = tnsAdmin;
        }

        var builder = new OracleConnectionStringBuilder(baseConnectionString)
        {
            ["Token Authentication"] = "OAUTH",
            ["Access Token"] = accessToken
        };

        return new OracleDbService(builder.ConnectionString);
    }

    // Private constructor for factory method
    private OracleDbService(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Opens a connection to the Oracle database.
    /// </summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        _connection = new OracleConnection(_connectionString);
        await _connection.OpenAsync(cancellationToken);
    }

    /// <summary>
    /// Executes a query and returns the results as a DataTable.
    /// </summary>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="parameters">Optional parameters for the query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A DataTable containing the query results.</returns>
    public async Task<DataTable> ExecuteQueryAsync(
        string sql,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        await using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.Add(new OracleParameter(param.Key, param.Value));
            }
        }

        var dataTable = new DataTable();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        dataTable.Load(reader);

        return dataTable;
    }

    /// <summary>
    /// Executes a query and returns results using a callback for each row.
    /// </summary>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="rowHandler">Action to handle each row.</param>
    /// <param name="parameters">Optional parameters for the query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExecuteQueryAsync(
        string sql,
        Action<IDataRecord> rowHandler,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        await using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.Add(new OracleParameter(param.Key, param.Value));
            }
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rowHandler(reader);
        }
    }

    /// <summary>
    /// Executes a scalar query and returns the first column of the first row.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="parameters">Optional parameters for the query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The scalar result.</returns>
    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        await using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.Add(new OracleParameter(param.Key, param.Value));
            }
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);

        if (result == null || result == DBNull.Value)
        {
            return default;
        }

        return (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>
    /// Executes a non-query command (INSERT, UPDATE, DELETE).
    /// </summary>
    /// <param name="sql">The SQL command to execute.</param>
    /// <param name="parameters">Optional parameters for the command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows affected.</returns>
    public async Task<int> ExecuteNonQueryAsync(
        string sql,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        await using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                command.Parameters.Add(new OracleParameter(param.Key, param.Value));
            }
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Tests the connection by executing a simple query.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the connection is successful.</returns>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken);
            var result = await ExecuteScalarAsync<int>("SELECT 1 FROM DUAL", cancellationToken: cancellationToken);
            return result == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the current database user.
    /// </summary>
    public async Task<string?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<string>("SELECT USER FROM DUAL", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets the database version.
    /// </summary>
    public async Task<string?> GetDatabaseVersionAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<string>(
            "SELECT BANNER FROM V$VERSION WHERE ROWNUM = 1",
            cancellationToken: cancellationToken);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection == null)
        {
            await OpenAsync(cancellationToken);
        }
        else if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
