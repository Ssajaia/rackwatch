using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace RackWatch;

// Login accounts in users.json, measurement history in history.db (both in Documents\RackWatch)
public static class Db
{
    static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RackWatch");
    static readonly string UsersFile = Path.Combine(Folder, "users.json");
    static readonly string Connection = "Data Source=" + Path.Combine(Folder, "history.db");

    static Db()
    {
        Directory.CreateDirectory(Folder);
        Execute("""
            CREATE TABLE IF NOT EXISTS readings (time TEXT, temp REAL, hum REAL, flame INTEGER, leak INTEGER, vent INTEGER, power INTEGER, state TEXT);
            CREATE TABLE IF NOT EXISTS alerts (time TEXT, type TEXT, level TEXT);
            """);
    }

    public static bool Login(string name, string password) =>
        LoadUsers().Any(u => u.Name == name && u.Hash == Hash(password));

    public static bool Register(string name, string password)
    {
        var users = LoadUsers();
        if (users.Any(u => u.Name == name)) return false;
        users.Add(new User(name, Hash(password)));
        File.WriteAllText(UsersFile, Json.Write(users));
        return true;
    }

    public static void Save(Status s) =>
        Execute("INSERT INTO readings VALUES ($time, $temp, $hum, $flame, $leak, $vent, $power, $state)",
            ("$time", DateTime.Now.ToString("s")), ("$temp", s.Temp), ("$hum", s.Hum), ("$flame", s.Flame),
            ("$leak", s.Leak), ("$vent", s.Vent), ("$power", s.Power), ("$state", s.State));

    public static void Save(Alert a) =>
        Execute("INSERT INTO alerts VALUES ($time, $type, $level)",
            ("$time", a.Time.ToString("s")), ("$type", a.Type), ("$level", a.Level));

    public static List<double> RecentTemps(TimeSpan period) =>
        Query($"SELECT temp FROM readings WHERE temp IS NOT NULL AND time >= '{DateTime.Now - period:s}' ORDER BY rowid DESC",
            r => r.GetDouble(0));

    public static List<Alert> RecentAlerts(int count) =>
        Query($"SELECT time, type, level FROM alerts ORDER BY rowid DESC LIMIT {count}",
            r => new Alert(r.GetString(1), r.GetString(2)) { Time = DateTime.Parse(r.GetString(0)) });

    static List<User> LoadUsers() =>
        File.Exists(UsersFile) ? Json.Parse<List<User>>(File.ReadAllText(UsersFile)) : [];

    static string Hash(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    static void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var db = new SqliteConnection(Connection);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    // Runs a newest-first query and returns the rows oldest first (ready for the chart and alert list)
    static List<T> Query<T>(string sql, Func<SqliteDataReader, T> read)
    {
        using var db = new SqliteConnection(Connection);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(read(reader));
        rows.Reverse();
        return rows;
    }
}
