using System.IO;
using GKSKLaiXe.Models;
using Microsoft.Data.Sqlite;

namespace GKSKLaiXe.Services;

public sealed class SentHistoryService
{
    private readonly string _dbPath;

    public SentHistoryService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GKSKLaiXe");

        Directory.CreateDirectory(folder);

        _dbPath = Path.Combine(folder, "sent_history.db");

        Initialize();
    }

    private string ConnectionString =>
        new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath
        }.ToString();

    private void Initialize()
    {
        using var cn = new SqliteConnection(ConnectionString);
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SentHistory
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SO TEXT NOT NULL,
                HANGBANGLAI TEXT NOT NULL,
                SourceDate TEXT NOT NULL,
                SentAt TEXT NOT NULL,
                UUID TEXT NULL,
                MSG_STATE TEXT NULL,
                MSG_TEXT TEXT NULL,
                UNIQUE(SO, HANGBANGLAI)
            );

            CREATE INDEX IF NOT EXISTS IX_SentHistory_SourceDate
                ON SentHistory(SourceDate);
            """;

        cmd.ExecuteNonQuery();
    }

    public HashSet<string> LoadSentKeys(DateTime sourceDate)
    {
        var result =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var cn = new SqliteConnection(ConnectionString);
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            SELECT SO, HANGBANGLAI
            FROM SentHistory
            WHERE SourceDate = $SourceDate;
            """;

        cmd.Parameters.AddWithValue(
            "$SourceDate",
            sourceDate.ToString("yyyy-MM-dd"));

        using var rd = cmd.ExecuteReader();

        while (rd.Read())
        {
            var so =
                rd.IsDBNull(0)
                    ? ""
                    : rd.GetString(0).Trim();

            var hang =
                rd.IsDBNull(1)
                    ? ""
                    : rd.GetString(1).Trim();

            result.Add(BuildKey(so, hang));
        }

        return result;
    }

    public void ApplySentFlags(
        IEnumerable<GkskRecord> records,
        DateTime sourceDate)
    {
        var sentKeys =
            LoadSentKeys(sourceDate);

        foreach (var record in records)
        {
            record.IsSent =
                sentKeys.Contains(
                    BuildKey(
                        record.SO,
                        record.HANGBANGLAI));

            if (record.IsSent &&
                string.IsNullOrWhiteSpace(record.SendStatus))
            {
                record.SendStatus = "Đã gửi";
            }
        }
    }

    public void ApplySentFlags(IEnumerable<GkskRecord> records)
    {
        foreach (var group in records.GroupBy(x => x.CreateDate?.Date ?? DateTime.Today))
        {
            ApplySentFlags(group, group.Key);
        }
    }

    // Dùng lại khi bật chức năng gửi API.
    // Chỉ gọi hàm này sau khi API trả thành công.
    public void MarkSent(
        GkskRecord record,
        DateTime sourceDate,
        string uuid = "",
        string msgState = "1",
        string msgText = "")
    {
        using var cn = new SqliteConnection(ConnectionString);
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO SentHistory
            (
                SO,
                HANGBANGLAI,
                SourceDate,
                SentAt,
                UUID,
                MSG_STATE,
                MSG_TEXT
            )
            VALUES
            (
                $SO,
                $HANGBANGLAI,
                $SourceDate,
                $SentAt,
                $UUID,
                $MSG_STATE,
                $MSG_TEXT
            )
            ON CONFLICT(SO, HANGBANGLAI)
            DO UPDATE SET
                SourceDate = excluded.SourceDate,
                SentAt = excluded.SentAt,
                UUID = excluded.UUID,
                MSG_STATE = excluded.MSG_STATE,
                MSG_TEXT = excluded.MSG_TEXT;
            """;

        cmd.Parameters.AddWithValue("$SO", (record.SO ?? "").Trim());
        cmd.Parameters.AddWithValue("$HANGBANGLAI", (record.HANGBANGLAI ?? "").Trim());
        cmd.Parameters.AddWithValue("$SourceDate", sourceDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$SentAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$UUID", uuid ?? "");
        cmd.Parameters.AddWithValue("$MSG_STATE", msgState ?? "");
        cmd.Parameters.AddWithValue("$MSG_TEXT", msgText ?? "");

        cmd.ExecuteNonQuery();
    }


    public IReadOnlyList<SentHistoryMatch> FindBySos(IEnumerable<string?> soValues)
    {
        var wanted = soValues
            .Select(NormalizeSo)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0)
            return Array.Empty<SentHistoryMatch>();

        var result = new List<SentHistoryMatch>();

        using var cn = new SqliteConnection(ConnectionString);
        cn.Open();

        using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            SELECT SO, HANGBANGLAI, SourceDate, SentAt, UUID, MSG_STATE, MSG_TEXT
            FROM SentHistory
            ORDER BY SentAt DESC;
            """;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var so = rd.IsDBNull(0) ? "" : rd.GetString(0).Trim();
            if (!wanted.Contains(NormalizeSo(so)))
                continue;

            result.Add(new SentHistoryMatch(
                SO: so,
                HANGBANGLAI: rd.IsDBNull(1) ? "" : rd.GetString(1).Trim(),
                SourceDate: rd.IsDBNull(2) ? "" : rd.GetString(2).Trim(),
                SentAt: rd.IsDBNull(3) ? "" : rd.GetString(3).Trim(),
                UUID: rd.IsDBNull(4) ? "" : rd.GetString(4).Trim(),
                MessageState: rd.IsDBNull(5) ? "" : rd.GetString(5).Trim(),
                MessageText: rd.IsDBNull(6) ? "" : rd.GetString(6).Trim()));
        }

        return result;
    }

    public bool WasSoSent(string? so)
    {
        return FindBySos([so]).Count > 0;
    }

    private static string NormalizeSo(string? so)
    {
        return (so ?? "").Trim().ToUpperInvariant();
    }

    private static string BuildKey(
        string? so,
        string? hangBangLai)
    {
        return
            $"{(so ?? "").Trim().ToUpperInvariant()}|" +
            $"{(hangBangLai ?? "").Trim().ToUpperInvariant()}";
    }
}


public sealed record SentHistoryMatch(
    string SO,
    string HANGBANGLAI,
    string SourceDate,
    string SentAt,
    string UUID,
    string MessageState,
    string MessageText);
