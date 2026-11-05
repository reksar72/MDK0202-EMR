using Ais.Core;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Ais.Infrastructure;
public sealed class SqlitePatientRepository(string path) : IPatientRepository
{
    SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Patients(ExternalId INTEGER PRIMARY KEY, Id TEXT NOT NULL UNIQUE, FullName TEXT NOT NULL, BirthDate TEXT NOT NULL, Sex TEXT NOT NULL, PolicyNumber TEXT NOT NULL, UpdatedAt TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
        return c;
    }

    static Patient Read(SqliteDataReader r) => new(r.GetInt32(0), Guid.Parse(r.GetString(1)), r.GetString(2), DateOnly.ParseExact(r.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture), r.GetString(4), r.GetString(5), DateTimeOffset.Parse(r.GetString(6), CultureInfo.InvariantCulture));
    public Patient? Find(Guid id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM Patients WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public string Upsert(Patient patient)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Patient? old;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT * FROM Patients WHERE ExternalId=$id";
            cmd.Parameters.AddWithValue("$id", patient.ExternalId);
            using var r = cmd.ExecuteReader();
            old = r.Read() ? Read(r) : null;
        }

        if (old != null)
        {
            if (old.Id != patient.Id)
                throw new InvalidOperationException("Конфликт привязки ExternalId к UUID");
            if (old.UpdatedAt > patient.UpdatedAt)
                return "STALE";
            if (old.UpdatedAt == patient.UpdatedAt)
            {
                if (old == patient)
                    return "DUPLICATE";
                throw new InvalidOperationException("Разные данные с одной версией");
            }
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Patients VALUES($ext,$id,$name,$birth,$sex,$policy,$at) ON CONFLICT(ExternalId) DO UPDATE SET FullName=$name,BirthDate=$birth,Sex=$sex,PolicyNumber=$policy,UpdatedAt=$at";
            cmd.Parameters.AddWithValue("$ext", patient.ExternalId);
            cmd.Parameters.AddWithValue("$id", patient.Id.ToString());
            cmd.Parameters.AddWithValue("$name", patient.FullName);
            cmd.Parameters.AddWithValue("$birth", patient.BirthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$sex", patient.Sex);
            cmd.Parameters.AddWithValue("$policy", patient.PolicyNumber);
            cmd.Parameters.AddWithValue("$at", patient.UpdatedAt.ToUniversalTime().ToString("O"));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return old == null ? "CREATED" : "UPDATED";
    }
}
