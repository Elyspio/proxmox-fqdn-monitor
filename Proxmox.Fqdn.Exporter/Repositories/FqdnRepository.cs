using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Proxmox.Fqdn.Exporter.Data;

namespace Proxmox.Fqdn.Exporter.Repositories;

public class FqdnRepository : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<FqdnRepository> _logger;

    public FqdnRepository(string dbPath, ILogger<FqdnRepository> logger)
    {
        _logger = logger;
        // On garde la connexion ouverte pour éviter la surcharge d'ouverture/fermeture
        // dans les scénarios haute performance
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        InitializeTable();
    }

    private void InitializeTable()
    {
        _logger.LogDebug($"Initializing {nameof(FqdnRepository)}");
        
        using var command = _connection.CreateCommand();
        // SQLite stocke les GUID et Dates en TEXT par défaut
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Fqdn (
                Id TEXT PRIMARY KEY,
                Ip TEXT NOT NULL,
                Hostname TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            
            -- Un index sur UpdatedAt est vital pour la vitesse de suppression
            CREATE INDEX IF NOT EXISTS IX_Fqdn_UpdatedAt ON Fqdn(UpdatedAt);
        ";
        command.ExecuteNonQuery();
    }

    public async Task AddRange(params IFqdnWithTimestamp[] dataList)
    {
        _logger.LogDebug("Adding {Length} FQDN entries to the database. {Data}", dataList.Length, string.Join(", ", dataList.Select(d => d.Hostname)));
        
        await using var transaction = await _connection.BeginTransactionAsync();
        
        await using var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO Fqdn (Id, Ip, Hostname, UpdatedAt) VALUES ($id, $ip, $host, $date)";

        var idParam = command.Parameters.Add("$id", SqliteType.Text);
        var ipParam = command.Parameters.Add("$ip", SqliteType.Text);
        var hostParam = command.Parameters.Add("$host", SqliteType.Text);
        var dateParam = command.Parameters.Add("$date", SqliteType.Text);

        foreach (var data in dataList)
        {
            idParam.Value = Guid.CreateVersion7().ToString();
            ipParam.Value = data.Ip;
            hostParam.Value = data.Hostname;
            dateParam.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
    
    public async Task DeleteOlderThan(DateTime thresholdDate)
    {
        _logger.LogDebug("Deleting FQDN entries older than {ThresholdDate}", thresholdDate);
        
        await using var command = _connection.CreateCommand();
        
        command.CommandText = "DELETE FROM Fqdn WHERE UpdatedAt < $threshold";

        command.Parameters.AddWithValue("threshold", thresholdDate.ToString("yyyy-MM-dd HH:mm:ss"));

        var nbDeleted  = await command.ExecuteNonQueryAsync(); // Retourne le nombre de lignes supprimées
        
        _logger.LogDebug("Deleted {NbDeleted} FQDN entries", nbDeleted);
    }
    

    public async Task<List<FqdnModel>> GetAll()
    {
        var results = new List<FqdnModel>();

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Id, Ip, Hostname, UpdatedAt FROM Fqdn";

        await using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            // Lecture colonne par colonne : Super rapide et zéro allocation dynamique
            var ip = reader.GetString(1);
            var host = reader.GetString(2);
            var dateStr = reader.GetString(3);

            // Reconstitution des types C#
            var entity = new FqdnModel(
                ip,
                host,
                DateTime.Parse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            );

            results.Add(entity);
        }

        _logger.LogDebug("Returning {Count} FQDN entries", results.Count);
        
        return results;
    }

    public void Dispose()
    {
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task DeleteAllByIps(string[] ips)
    {
        _logger.LogDebug("{Method} FQDN entries with ips: {ips}", nameof(DeleteAllByIps), string.Join(", ", ips));
        
        await using var command = _connection.CreateCommand();
        
        // Construction dynamique de la requête avec autant de paramètres que de hostnames
        var parameters = string.Join(", ", ips.Select((_, i) => $"$host{i}"));
        command.CommandText = $"DELETE FROM Fqdn WHERE Ip  IN ({parameters})";

        
        
        for (var i = 0; i < ips.Length; i++)
        {
            command.Parameters.AddWithValue($"host{i}", ips[i]);
        }

        var nbDeleted  = await command.ExecuteNonQueryAsync(); // Retourne le nombre de lignes supprimées
        
        _logger.LogDebug("{Method} {NbDeleted} FQDN entries", nameof(DeleteAllByIps), nbDeleted);
    }
}