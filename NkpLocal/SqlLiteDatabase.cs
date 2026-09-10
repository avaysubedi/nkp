using Microsoft.Data.Sqlite;

public class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(string dbPath = "nkp.db")
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }
}
