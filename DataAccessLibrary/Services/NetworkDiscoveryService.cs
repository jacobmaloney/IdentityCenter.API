using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DataAccessLibrary.Services;

public class NetworkDiscoveryService : INetworkDiscoveryService
{
    private readonly ISqlLicenseRepository _repo;
    private readonly IAuditLogService _auditLog;
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    // Synthetic connection ID for network-discovered servers (no real AD sync)
    private static readonly Guid DiscoveredAssetsConnectionId =
        Guid.Parse("D1500000-0000-0000-0000-000000000001");

    public NetworkDiscoveryService(
        ISqlLicenseRepository repo,
        IAuditLogService auditLog,
        IConfiguration config,
        IGlobalLogger logger)
    {
        _repo = repo;
        _auditLog = auditLog;
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing");
        _logger = logger;
    }

    public async Task<NetworkScanResult> ScanCidrAsync(
        string cidr, Guid? rangeId = null, int timeoutMs = 500, CancellationToken ct = default)
    {
        var result = new NetworkScanResult { CidrRange = cidr };
        var sw = Stopwatch.StartNew();

        try
        {
            var ipList = ExpandCidr(cidr);
            result.TotalScanned = ipList.Count;

            _logger.LogInformation("NetworkDiscovery: scanning {Count} IPs in {Cidr}", ipList.Count, cidr);

            // Phase 1: Probe each IP for TCP/1433 in parallel
            var hits = new ConcurrentBag<(string Ip, string? Hostname)>();
            var parallelism = Math.Min(32, ipList.Count);
            using var semaphore = new SemaphoreSlim(parallelism);

            var tasks = ipList.Select(async ip =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    if (await ProbeSqlPortAsync(ip, 1433, timeoutMs, ct))
                    {
                        var hostname = await TryReverseLookupAsync(ip);
                        hits.Add((ip, hostname));
                        _logger.LogInformation("NetworkDiscovery: hit {Ip} ({Host})", ip, hostname ?? "(no DNS)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "NetworkDiscovery: error probing {Ip}", ip);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            result.FoundServers = hits.Count;
            var hitIps = new HashSet<string>(hits.Select(h => h.Ip));

            // Phase 2: Upsert each hit into SqlServerInventory + create Objects for unmanaged hosts
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Mark previously-discovered servers in this CIDR range as Offline if they didn't respond
            // to this scan. Keeps them in the list instead of silently disappearing — a rescan will
            // bring them back online if they start responding again.
            var ipListSet = new HashSet<string>(ipList);
            var previouslyKnown = await conn.QueryAsync<(Guid Id, string IpAddress, string ServerName)>(
                @"SELECT Id, IpAddress, ServerName FROM SqlServerInventory
                  WHERE IpAddress IS NOT NULL AND IpAddress IN @Ips AND IsOnline = 1",
                new { Ips = ipList });

            foreach (var prev in previouslyKnown)
            {
                if (!hitIps.Contains(prev.IpAddress))
                {
                    // Previously-known server didn't respond — mark offline but keep the row
                    await conn.ExecuteAsync(@"
                        UPDATE SqlServerInventory
                        SET IsOnline = 0,
                            LastScanStatus = 'Offline',
                            LastScanMessage = 'Did not respond to network scan on ' + CONVERT(NVARCHAR(30), GETUTCDATE(), 120),
                            UpdatedAt = GETUTCDATE()
                        WHERE Id = @Id",
                        new { Id = prev.Id });

                    result.OfflineServers++;
                    _logger.LogInformation("NetworkDiscovery: {Ip} ({Server}) marked Offline — did not respond", prev.IpAddress, prev.ServerName);

                    // Audit the transition
                    try
                    {
                        await _auditLog.LogChangeAsync(new ChangeAuditEntry
                        {
                            OperationType = ChangeOperationType.Update,
                            EntityType = "SqlServerInventory",
                            EntityId = prev.Id,
                            EntityDisplayName = prev.ServerName,
                            PropertyName = "IsOnline",
                            OldValue = "true",
                            NewValue = "false",
                            Reason = string.Concat("Did not respond to network scan of ", cidr),
                            Source = "NetworkDiscovery",
                            UserDisplayName = "System (NetworkScan)"
                        });
                    }
                    catch (Exception auditEx)
                    {
                        _logger.LogWarning(auditEx, "NetworkDiscovery: failed to log offline transition for {InventoryId}", prev.Id);
                    }
                }
            }

            // Ensure the synthetic "Discovered Assets" connection exists (FK target for new Objects)
            if (hits.Any())
            {
                await EnsureDiscoveredAssetsConnectionAsync(conn, ct);
            }

            foreach (var (ip, rawHostname) in hits)
            {
                if (ct.IsCancellationRequested) break;

                // Clean up bad reverse DNS results before using them
                string? hostname = rawHostname;
                if (!string.IsNullOrEmpty(hostname) && LooksLikeDomainZone(hostname))
                {
                    _logger.LogWarning("NetworkDiscovery: ignoring suspicious reverse DNS result '{Host}' for {Ip}", hostname, ip);
                    hostname = null;
                }

                var discovered = new DiscoveredServer
                {
                    IpAddress = ip,
                    Hostname = hostname,
                    Port = 1433
                };

                var canonicalName = !string.IsNullOrEmpty(hostname)
                    ? StripDomain(hostname)
                    : ip;

                bool isNew = false; // Determined below after MERGE

                // Try to find an existing computer Object matching this host via multiple strategies
                Guid? objectId = null;
                string? objectDisplayName = null;
                bool isObjectNew = false;

                var objMatch = await FindMatchingComputerAsync(conn, canonicalName, hostname, ip);
                if (objMatch.HasValue)
                {
                    objectId = objMatch.Value.Id;
                    objectDisplayName = objMatch.Value.DisplayName ?? canonicalName;

                    if (!string.IsNullOrEmpty(objMatch.Value.CN))
                    {
                        canonicalName = objMatch.Value.CN;
                    }
                }

                // Also check: does SqlServerInventory already have this IP linked to a DIFFERENT Object?
                // (e.g., from a prior scan that matched the AD Object correctly)
                if (objectId == null)
                {
                    var existingObjId = await conn.QuerySingleOrDefaultAsync<string?>(
                        "SELECT TOP 1 ObjectId FROM SqlServerInventory WHERE IpAddress = @Ip AND ObjectId IS NOT NULL",
                        new { Ip = ip });
                    if (!string.IsNullOrEmpty(existingObjId) && Guid.TryParse(existingObjId, out var linkedId))
                    {
                        var linkedObj = await conn.QuerySingleOrDefaultAsync<(Guid Id, string? CN, string? DisplayName)?>(
                            "SELECT Id, CN, DisplayName FROM Objects WHERE Id = @Id AND DeletedAt IS NULL",
                            new { Id = linkedId });
                        if (linkedObj.HasValue)
                        {
                            objectId = linkedObj.Value.Id;
                            objectDisplayName = linkedObj.Value.DisplayName;
                            if (!string.IsNullOrEmpty(linkedObj.Value.CN)) canonicalName = linkedObj.Value.CN;
                            _logger.LogInformation("NetworkDiscovery: {Ip} already linked to Object {CN} via SqlServerInventory", ip, canonicalName);
                        }
                    }
                }

                // If STILL no matching Object exists, create one under the "Discovered Assets" synthetic connection
                // Also check for soft-deleted Objects with the same SourceUniqueId and reactivate them
                if (objectId == null)
                {
                    var now = DateTime.UtcNow;
                    var sourceUniqueId = string.Concat("net:", ip);
                    objectDisplayName = string.IsNullOrEmpty(hostname) ? canonicalName : hostname;

                    // Check for a soft-deleted Object first (from a previous delete + rescan cycle)
                    var softDeletedId = await conn.QuerySingleOrDefaultAsync<Guid?>(
                        @"SELECT Id FROM Objects
                          WHERE SourceConnectionId = @ConnId AND SourceUniqueId = @SourceUniqueId AND DeletedAt IS NOT NULL",
                        new { ConnId = DiscoveredAssetsConnectionId, SourceUniqueId = sourceUniqueId });

                    if (softDeletedId.HasValue)
                    {
                        // Reactivate the soft-deleted Object with updated name
                        objectId = softDeletedId.Value;
                        await conn.ExecuteAsync(@"
                            UPDATE Objects
                            SET DeletedAt = NULL, IsActive = 1, CN = @CN, DisplayName = @DisplayName, LastSyncedAt = @Now
                            WHERE Id = @Id",
                            new { Id = objectId.Value, CN = canonicalName, DisplayName = objectDisplayName, Now = now });
                        isObjectNew = false; // reactivated, not truly new
                        _logger.LogInformation("NetworkDiscovery: reactivated soft-deleted Object {ObjectId} for {Name}", objectId, canonicalName);
                    }
                    else
                    {
                        objectId = Guid.NewGuid();
                        isObjectNew = true;

                        await conn.ExecuteAsync(@"
                            INSERT INTO Objects
                                (Id, SourceConnectionId, SourceUniqueId, SourceType, ObjectClass,
                                 CN, DisplayName,
                                 IsActive, IsAuthoritative, IsBuiltIn, IsAdminSDHolder, PasswordNeverExpires, IsHighRisk,
                                 MatchConfidence,
                                 FirstSyncedAt, LastSyncedAt, CreatedAt)
                            VALUES
                                (@Id, @ConnId, @SourceUniqueId, 'NetworkDiscovery', 'computer',
                                 @CN, @DisplayName,
                                 1, 1, 0, 0, 0, 0,
                                 100,
                                 @Now, @Now, @Now)",
                            new
                            {
                                Id = objectId.Value,
                                ConnId = DiscoveredAssetsConnectionId,
                                SourceUniqueId = sourceUniqueId,
                                CN = canonicalName,
                                DisplayName = objectDisplayName,
                                Now = now
                            });

                        result.NewObjects++;
                        _logger.LogInformation("NetworkDiscovery: created Object {ObjectId} for {Name}", objectId, canonicalName);
                    }

                    // Attach IP + DNS hostname as attributes (only for newly created Objects)
                    if (isObjectNew)
                    {
                        if (!string.IsNullOrEmpty(hostname))
                        {
                            await conn.ExecuteAsync(@"
                                INSERT INTO ObjectAttributes (Id, ObjectId, AttributeName, AttributeValue, DataType, LastSyncedAt)
                                VALUES (NEWID(), @ObjectId, 'dNSHostName', @Value, 'string', @Now)",
                                new { ObjectId = objectId.Value, Value = hostname, Now = now });
                        }

                        await conn.ExecuteAsync(@"
                            INSERT INTO ObjectAttributes (Id, ObjectId, AttributeName, AttributeValue, DataType, LastSyncedAt)
                            VALUES (NEWID(), @ObjectId, 'ipHostNumber', @Value, 'string', @Now)",
                            new { ObjectId = objectId.Value, Value = ip, Now = now });
                    }
                }

                // Idempotent upsert: MERGE-style logic using direct SQL.
                // Matches by (ServerName+Port) OR IpAddress to avoid duplicate rows from prior scans,
                // and uses MERGE for atomic insert-or-update to prevent race conditions between rescans.
                Guid savedId;
                var upsertParams = new
                {
                    ObjectId = objectId?.ToString(),
                    ServerName = canonicalName,
                    Fqdn = hostname,
                    IpAddress = ip,
                    NewId = Guid.NewGuid()
                };

                const string mergeSql = @"
                    MERGE SqlServerInventory AS target
                    USING (SELECT @ServerName AS ServerName, @IpAddress AS IpAddress) AS src
                    ON (target.ServerName = src.ServerName AND ISNULL(target.Port, 1433) = 1433)
                       OR (src.IpAddress IS NOT NULL AND target.IpAddress = src.IpAddress)
                    WHEN MATCHED THEN
                        UPDATE SET
                            ObjectId = COALESCE(@ObjectId, target.ObjectId),
                            DiscoveryMethod = 'NetworkScan',
                            ServerName = @ServerName,
                            Fqdn = COALESCE(@Fqdn, target.Fqdn),
                            IpAddress = COALESCE(@IpAddress, target.IpAddress),
                            Port = 1433,
                            IsOnline = 1,
                            LastScanStatus = CASE WHEN target.LastScanStatus = 'Offline' THEN 'Success' ELSE target.LastScanStatus END,
                            LastScanMessage = CASE WHEN target.LastScanStatus = 'Offline' THEN 'Back online' ELSE target.LastScanMessage END,
                            LastDiscoveredAt = GETUTCDATE(),
                            UpdatedAt = GETUTCDATE()
                    WHEN NOT MATCHED THEN
                        INSERT (Id, ObjectId, DiscoveryMethod, ServerName, Fqdn, IpAddress, Port,
                                IsOnline, DiscoveryStatus, LastDiscoveredAt, CreatedAt, UpdatedAt)
                        VALUES (@NewId, @ObjectId, 'NetworkScan', @ServerName, @Fqdn, @IpAddress, 1433,
                                1, 'Discovered', GETUTCDATE(), GETUTCDATE(), GETUTCDATE())
                    OUTPUT INSERTED.Id;";

                savedId = await conn.QuerySingleAsync<Guid>(mergeSql, upsertParams);

                // Derive isNew from whether the MERGE inserted or matched
                isNew = savedId == upsertParams.NewId;

                discovered.SqlInventoryId = savedId;
                discovered.ObjectId = objectId;
                discovered.ObjectDisplayName = objectDisplayName;
                discovered.IsNew = isNew;
                discovered.IsObjectNew = isObjectNew;

                // ── Change History tracking ──
                // Log creation of new computer Objects so standard audit trail sees them
                if (isObjectNew && objectId.HasValue)
                {
                    try
                    {
                        await _auditLog.LogChangeAsync(new ChangeAuditEntry
                        {
                            OperationType = ChangeOperationType.Create,
                            EntityType = "Object",
                            EntityId = objectId.Value,
                            EntityDisplayName = objectDisplayName ?? canonicalName,
                            NewValue = string.Concat("Computer (IP: ", ip, ", Hostname: ", hostname ?? "(none)", ")"),
                            Reason = string.Concat("Auto-created from network discovery scan of ", cidr),
                            Source = "NetworkDiscovery",
                            UserDisplayName = "System (NetworkScan)"
                        });
                    }
                    catch (Exception auditEx)
                    {
                        _logger.LogWarning(auditEx, "NetworkDiscovery: failed to log audit for new Object {ObjectId}", objectId);
                    }
                }

                // Log creation of new SqlServerInventory rows
                if (isNew)
                {
                    try
                    {
                        await _auditLog.LogChangeAsync(new ChangeAuditEntry
                        {
                            OperationType = ChangeOperationType.Create,
                            EntityType = "SqlServerInventory",
                            EntityId = savedId,
                            EntityDisplayName = canonicalName,
                            NewValue = string.Concat("SQL server discovered at ", ip, ":1433"),
                            RelatedEntityId = objectId,
                            RelatedEntityName = objectDisplayName,
                            Reason = string.Concat("Network scan of ", cidr, " found SQL on TCP/1433"),
                            Source = "NetworkDiscovery",
                            UserDisplayName = "System (NetworkScan)"
                        });
                    }
                    catch (Exception auditEx)
                    {
                        _logger.LogWarning(auditEx, "NetworkDiscovery: failed to log audit for new inventory {InventoryId}", savedId);
                    }
                }
                else
                {
                    // Updated existing — log the rescan
                    try
                    {
                        await _auditLog.LogChangeAsync(new ChangeAuditEntry
                        {
                            OperationType = ChangeOperationType.Update,
                            EntityType = "SqlServerInventory",
                            EntityId = savedId,
                            EntityDisplayName = canonicalName,
                            PropertyName = "LastDiscoveredAt",
                            NewValue = DateTime.UtcNow.ToString("o"),
                            RelatedEntityId = objectId,
                            RelatedEntityName = objectDisplayName,
                            Reason = string.Concat("Rescan of ", cidr),
                            Source = "NetworkDiscovery",
                            UserDisplayName = "System (NetworkScan)"
                        });
                    }
                    catch (Exception auditEx)
                    {
                        _logger.LogWarning(auditEx, "NetworkDiscovery: failed to log audit for rescan {InventoryId}", savedId);
                    }
                }

                if (isNew) result.NewServers++;
                else result.ExistingServers++;

                result.Discovered.Add(discovered);
            }

            // Update LastScannedAt on the range if provided
            if (rangeId.HasValue)
            {
                await conn.ExecuteAsync(
                    @"UPDATE NetworkScanRanges
                      SET LastScannedAt = @Now, LastScanDurationSeconds = @Duration
                      WHERE Id = @Id",
                    new { Id = rangeId.Value, Now = DateTime.UtcNow, Duration = (int)sw.Elapsed.TotalSeconds });
            }

            result.Success = true;
            result.DurationSeconds = (int)sw.Elapsed.TotalSeconds;

            _logger.LogInformation(
                "NetworkDiscovery: {Cidr} — scanned {Total}, found {Found}, {New} new servers, {NewObj} new objects, {Existing} updated ({Duration}s)",
                cidr, result.TotalScanned, result.FoundServers, result.NewServers, result.NewObjects, result.ExistingServers, result.DurationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NetworkDiscovery: failed for {Cidr}", cidr);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.DurationSeconds = (int)sw.Elapsed.TotalSeconds;
        }

        // Record scan history (always — success or failure)
        try
        {
            var historyJson = result.Discovered.Any()
                ? System.Text.Json.JsonSerializer.Serialize(result.Discovered)
                : null;

            await _repo.RecordScanHistoryAsync(new NetworkScanHistoryEntry
            {
                NetworkScanRangeId = rangeId,
                CidrRange = cidr,
                StartedAt = DateTime.UtcNow.AddSeconds(-result.DurationSeconds),
                CompletedAt = DateTime.UtcNow,
                DurationSeconds = result.DurationSeconds,
                Status = result.Success ? "Success" : "Failed",
                TotalScanned = result.TotalScanned,
                FoundServers = result.FoundServers,
                NewServers = result.NewServers,
                ExistingServers = result.ExistingServers,
                NewObjectsCreated = result.NewObjects,
                ErrorMessage = result.ErrorMessage,
                DiscoveredServersJson = historyJson,
                TriggeredBy = "WebPortal"
            });
        }
        catch (Exception histEx)
        {
            _logger.LogWarning(histEx, "NetworkDiscovery: failed to record scan history for {Cidr}", cidr);
        }

        return result;
    }

    /// <summary>
    /// Multi-strategy match for an existing computer Object:
    /// 1. CN exact (short name)
    /// 2. DisplayName exact
    /// 3. dNSHostName attribute contains the short name (covers AD-synced FQDNs like "SERVER01.domain.local")
    /// 4. MSSQLSvc SPN contains the short name (SQL cluster / alias discovery)
    /// 5. ipHostNumber attribute matches the IP
    /// Returns null if nothing matches.
    /// </summary>
    private async Task<(Guid Id, string? CN, string? DisplayName)?> FindMatchingComputerAsync(
        SqlConnection conn, string shortName, string? fqdn, string ip)
    {
        // Strategy 1+2: exact CN or DisplayName match on the short name
        var match = await conn.QuerySingleOrDefaultAsync<(Guid Id, string? CN, string? DisplayName)?>(
            @"SELECT TOP 1 Id, CN, DisplayName FROM Objects
              WHERE ObjectClass = 'computer' AND DeletedAt IS NULL
                AND (CN = @Name OR DisplayName = @Name)",
            new { Name = shortName });
        if (match.HasValue) return match;

        // Strategy 3: dNSHostName attribute contains the short name
        // AD sync stores "SERVER01.domain.local" as dNSHostName; short-name match on the prefix
        var byDns = await conn.QuerySingleOrDefaultAsync<(Guid Id, string? CN, string? DisplayName)?>(
            @"SELECT TOP 1 o.Id, o.CN, o.DisplayName
              FROM Objects o
              JOIN ObjectAttributes a ON a.ObjectId = o.Id
              WHERE o.ObjectClass = 'computer' AND o.DeletedAt IS NULL
                AND a.AttributeName = 'dNSHostName'
                AND (a.AttributeValue = @Fqdn
                     OR a.AttributeValue LIKE @NamePrefix
                     OR a.AttributeValue LIKE @NameAnywhere)",
            new
            {
                Fqdn = fqdn ?? shortName,
                NamePrefix = shortName + ".%",
                NameAnywhere = "%" + shortName + "%"
            });
        if (byDns.HasValue) return byDns;

        // Strategy 4: MSSQLSvc SPN contains the short name (common for SQL clusters)
        // e.g. "MSSQLSvc/SQLPROD01.domain.local:1433"
        var bySpn = await conn.QuerySingleOrDefaultAsync<(Guid Id, string? CN, string? DisplayName)?>(
            @"SELECT TOP 1 o.Id, o.CN, o.DisplayName
              FROM Objects o
              JOIN ObjectAttributes a ON a.ObjectId = o.Id
              WHERE o.ObjectClass = 'computer' AND o.DeletedAt IS NULL
                AND a.AttributeName = 'servicePrincipalName'
                AND (a.AttributeValue LIKE @SqlPrefix OR a.AttributeValue LIKE @SqlAnywhere)",
            new
            {
                SqlPrefix = "MSSQLSvc/" + shortName + "%",
                SqlAnywhere = "%MSSQLSvc/%" + shortName + "%"
            });
        if (bySpn.HasValue) return bySpn;

        // Strategy 5: ipHostNumber attribute matches the IP
        var byIp = await conn.QuerySingleOrDefaultAsync<(Guid Id, string? CN, string? DisplayName)?>(
            @"SELECT TOP 1 o.Id, o.CN, o.DisplayName
              FROM Objects o
              JOIN ObjectAttributes a ON a.ObjectId = o.Id
              WHERE o.ObjectClass = 'computer' AND o.DeletedAt IS NULL
                AND a.AttributeName IN ('ipHostNumber', 'IPv4Address', 'ipAddress')
                AND a.AttributeValue = @Ip",
            new { Ip = ip });
        return byIp;
    }

    /// <summary>
    /// Detects reverse-DNS results that look like a zone name rather than a real host.
    /// Examples: "domain.local", "contoso.com", "internal.net" — no host prefix.
    /// A real hostname should have 3+ segments (host.domain.tld) or be a single name (non-FQDN).
    /// </summary>
    private static bool LooksLikeDomainZone(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var segments = name.Split('.');
        // Two segments like "domain.local" or "contoso.com" with a common tld-ish suffix
        if (segments.Length == 2)
        {
            var tld = segments[1].ToLowerInvariant();
            var knownDomainTlds = new[] { "local", "internal", "com", "net", "org", "corp", "lan", "home" };
            return knownDomainTlds.Contains(tld);
        }
        return false;
    }

    private async Task EnsureDiscoveredAssetsConnectionAsync(SqlConnection conn, CancellationToken ct)
    {
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM DirectoryConnections WHERE Id = @Id",
            new { Id = DiscoveredAssetsConnectionId });

        if (exists > 0) return;

        _logger.LogInformation("NetworkDiscovery: creating synthetic 'Discovered Assets' DirectoryConnection");

        await conn.ExecuteAsync(@"
            INSERT INTO DirectoryConnections
                (Id, Name, ConnectionType, ConnectionString, Credentials, IsActive, IsAuthoritative, CreatedAt)
            VALUES
                (@Id, 'Discovered Assets', 'NetworkDiscovery', '', '', 1, 0, GETUTCDATE())",
            new { Id = DiscoveredAssetsConnectionId });
    }

    private static async Task<string> GetExistingStatusAsync(SqlConnection conn, Guid inventoryId)
    {
        var status = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT DiscoveryStatus FROM SqlServerInventory WHERE Id = @Id", new { Id = inventoryId });
        // Preserve existing Managed/Approved status on rescan; only NEW rows become "Discovered"
        return string.IsNullOrEmpty(status) ? "Managed" : status;
    }

    private static async Task<bool> ProbeSqlPortAsync(string ip, int port, int timeoutMs, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await tcp.ConnectAsync(ip, port, cts.Token);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> TryReverseLookupAsync(string ip)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            return entry.HostName;
        }
        catch
        {
            return null;
        }
    }

    private static string StripDomain(string fqdn)
    {
        var dotIdx = fqdn.IndexOf('.');
        return dotIdx > 0 ? fqdn.Substring(0, dotIdx) : fqdn;
    }

    /// <summary>
    /// Expand a CIDR notation (e.g. "192.168.1.0/24") into a list of host IPs.
    /// Skips network (.0) and broadcast (.255) addresses for /24 and shorter prefixes.
    /// </summary>
    public static List<string> ExpandCidr(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2) throw new ArgumentException($"Invalid CIDR: {cidr}");

        var baseIp = IPAddress.Parse(parts[0]);
        var prefixLength = int.Parse(parts[1]);

        if (prefixLength < 0 || prefixLength > 32) throw new ArgumentException($"Invalid prefix length: {prefixLength}");

        var ipBytes = baseIp.GetAddressBytes();
        if (BitConverter.IsLittleEndian) Array.Reverse(ipBytes);
        var ipInt = BitConverter.ToUInt32(ipBytes, 0);

        var hostBits = 32 - prefixLength;
        var networkMask = hostBits == 32 ? 0u : 0xFFFFFFFF << hostBits;
        var networkInt = ipInt & networkMask;
        var broadcastInt = networkInt | (0xFFFFFFFF >> prefixLength);

        // Skip network and broadcast for /30 and wider
        var start = hostBits >= 2 ? networkInt + 1 : networkInt;
        var end = hostBits >= 2 ? broadcastInt - 1 : broadcastInt;

        var ips = new List<string>();
        for (var i = start; i <= end; i++)
        {
            var bytes = BitConverter.GetBytes(i);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            ips.Add(new IPAddress(bytes).ToString());
        }
        return ips;
    }
}
