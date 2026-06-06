using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;
using System.Text.RegularExpressions;

namespace DataAccessLibrary.Services.Chat;

/// <summary>
/// ChatBot service with real command implementations.
/// Supports natural language commands for user lookup, password reset, account management, and group operations.
/// </summary>
public class ChatBotService : IChatBotService
{
    private readonly ILogger<ChatBotService> _logger;
    private readonly string _connectionString;
    private readonly IDirectoryWriteService? _directoryWriteService;
    private readonly IObjectWriteBackService? _writeBackService;

    public ChatBotService(
        ILogger<ChatBotService> logger,
        IConfiguration configuration,
        IDirectoryWriteService? directoryWriteService = null,
        IObjectWriteBackService? writeBackService = null)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _directoryWriteService = directoryWriteService;
        _writeBackService = writeBackService;
    }

    public async Task<ChatResponse> SendMessageAsync(string message, string userId)
    {
        _logger.LogInformation("Processing chat message from user {UserId}: {Message}", userId, message);

        var normalizedMessage = message.Trim().ToLowerInvariant();

        try
        {
            // Help command
            if (IsHelpCommand(normalizedMessage))
            {
                return GetHelpResponse();
            }

            // Lookup user command: "lookup user john" or "find user john.smith" or "who is john"
            if (TryParseLookupCommand(normalizedMessage, out var searchTerm))
            {
                return await LookupUserAsync(searchTerm);
            }

            // Reset password command: "reset password for john.smith"
            if (TryParseResetPasswordCommand(normalizedMessage, out var resetTarget))
            {
                return await ResetPasswordAsync(resetTarget);
            }

            // Disable user command: "disable user john.smith" or "disable account john"
            if (TryParseDisableCommand(normalizedMessage, out var disableTarget))
            {
                return await DisableUserAsync(disableTarget);
            }

            // Enable user command: "enable user john.smith" or "enable account john"
            if (TryParseEnableCommand(normalizedMessage, out var enableTarget))
            {
                return await EnableUserAsync(enableTarget);
            }

            // Add to group command: "add john to VPN Users" or "add john.smith to group Administrators"
            if (TryParseAddToGroupCommand(normalizedMessage, out var addUser, out var addGroup))
            {
                return await AddUserToGroupAsync(addUser, addGroup);
            }

            // Remove from group command: "remove john from VPN Users"
            if (TryParseRemoveFromGroupCommand(normalizedMessage, out var removeUser, out var removeGroup))
            {
                return await RemoveUserFromGroupAsync(removeUser, removeGroup);
            }

            // List groups command: "groups for john" or "show groups for john.smith"
            if (TryParseListGroupsCommand(normalizedMessage, out var groupsTarget))
            {
                return await ListUserGroupsAsync(groupsTarget);
            }

            // Search groups command: "search groups vpn" or "find group admin"
            if (TryParseSearchGroupsCommand(normalizedMessage, out var groupSearch))
            {
                return await SearchGroupsAsync(groupSearch);
            }

            // Sync status command
            if (IsSyncStatusCommand(normalizedMessage))
            {
                return await GetSyncStatusAsync();
            }

            // Unknown command
            return new ChatResponse
            {
                Message = $"I didn't understand that command. Try:\n" +
                         $"• **lookup user [name]** - Find a user\n" +
                         $"• **reset password for [name]** - Reset a user's password\n" +
                         $"• **disable user [name]** - Disable an account\n" +
                         $"• **enable user [name]** - Enable an account\n" +
                         $"• **add [user] to [group]** - Add user to group\n" +
                         $"• **groups for [name]** - Show user's groups\n" +
                         $"• **help** - Show all commands",
                CardResults = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat command: {Message}", message);
            return new ChatResponse
            {
                Message = $"An error occurred while processing your request: {ex.Message}",
                CardResults = null
            };
        }
    }

    #region Command Parsers

    private bool IsHelpCommand(string message)
    {
        return message == "help" || message == "?" || message == "commands" || message.StartsWith("help ");
    }

    private bool TryParseLookupCommand(string message, out string searchTerm)
    {
        searchTerm = string.Empty;
        var patterns = new[]
        {
            @"^(?:lookup|find|search|who is|show)\s+(?:user\s+)?(.+)$",
            @"^user\s+(.+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                searchTerm = match.Groups[1].Value.Trim();
                return !string.IsNullOrWhiteSpace(searchTerm);
            }
        }
        return false;
    }

    private bool TryParseResetPasswordCommand(string message, out string target)
    {
        target = string.Empty;
        var patterns = new[]
        {
            @"^reset\s+password\s+(?:for\s+)?(.+)$",
            @"^password\s+reset\s+(?:for\s+)?(.+)$",
            @"^new\s+password\s+(?:for\s+)?(.+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                target = match.Groups[1].Value.Trim();
                return !string.IsNullOrWhiteSpace(target);
            }
        }
        return false;
    }

    private bool TryParseDisableCommand(string message, out string target)
    {
        target = string.Empty;
        var patterns = new[]
        {
            @"^disable\s+(?:user|account)\s+(.+)$",
            @"^deactivate\s+(?:user|account\s+)?(.+)$",
            @"^lock\s+(?:user|account\s+)?(.+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                target = match.Groups[1].Value.Trim();
                return !string.IsNullOrWhiteSpace(target);
            }
        }
        return false;
    }

    private bool TryParseEnableCommand(string message, out string target)
    {
        target = string.Empty;
        var patterns = new[]
        {
            @"^enable\s+(?:user|account)\s+(.+)$",
            @"^activate\s+(?:user|account\s+)?(.+)$",
            @"^unlock\s+(?:user|account\s+)?(.+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                target = match.Groups[1].Value.Trim();
                return !string.IsNullOrWhiteSpace(target);
            }
        }
        return false;
    }

    private bool TryParseAddToGroupCommand(string message, out string user, out string group)
    {
        user = string.Empty;
        group = string.Empty;
        var patterns = new[]
        {
            @"^add\s+(.+?)\s+to\s+(?:group\s+)?(.+)$",
            @"^assign\s+(.+?)\s+to\s+(?:group\s+)?(.+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                user = match.Groups[1].Value.Trim();
                group = match.Groups[2].Value.Trim();
                return !string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(group);
            }
        }
        return false;
    }

    private bool TryParseRemoveFromGroupCommand(string message, out string user, out string group)
    {
        user = string.Empty;
        group = string.Empty;
        var patterns = new[]
        {
            @"^remove\s+(.+?)\s+from\s+(?:group\s+)?(.+)$",
            @"^unassign\s+(.+?)\s+from\s+(?:group\s+)?(.+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                user = match.Groups[1].Value.Trim();
                group = match.Groups[2].Value.Trim();
                return !string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(group);
            }
        }
        return false;
    }

    private bool TryParseListGroupsCommand(string message, out string target)
    {
        target = string.Empty;
        var patterns = new[]
        {
            @"^(?:show\s+)?groups\s+(?:for\s+)?(.+)$",
            @"^list\s+groups\s+(?:for\s+)?(.+)$",
            @"^what\s+groups\s+(?:is|does)\s+(.+?)\s+(?:in|have)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                target = match.Groups[1].Value.Trim();
                return !string.IsNullOrWhiteSpace(target);
            }
        }
        return false;
    }

    private bool TryParseSearchGroupsCommand(string message, out string searchTerm)
    {
        searchTerm = string.Empty;
        var patterns = new[]
        {
            @"^(?:search|find)\s+groups?\s+(.+)$",
            @"^groups?\s+(?:named|like|matching)\s+(.+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                searchTerm = match.Groups[1].Value.Trim();
                return !string.IsNullOrWhiteSpace(searchTerm);
            }
        }
        return false;
    }

    private bool IsSyncStatusCommand(string message)
    {
        return message.Contains("sync") && (message.Contains("status") || message.Contains("running") || message.Contains("progress"));
    }

    #endregion

    #region Command Implementations

    private ChatResponse GetHelpResponse()
    {
        return new ChatResponse
        {
            Message = "## Available Commands\n\n" +
                     "**User Lookup**\n" +
                     "• `lookup user [name]` - Find a user by name, email, or username\n" +
                     "• `who is [name]` - Same as lookup\n\n" +
                     "**Account Management**\n" +
                     "• `reset password for [name]` - Generate new password\n" +
                     "• `disable user [name]` - Disable an account\n" +
                     "• `enable user [name]` - Enable an account\n\n" +
                     "**Group Management**\n" +
                     "• `add [user] to [group]` - Add user to a group\n" +
                     "• `remove [user] from [group]` - Remove user from group\n" +
                     "• `groups for [name]` - List user's group memberships\n" +
                     "• `search groups [term]` - Find groups by name\n\n" +
                     "**System**\n" +
                     "• `sync status` - Show current sync status\n" +
                     "• `help` - Show this help message",
            CardResults = null
        };
    }

    private async Task<ChatResponse> LookupUserAsync(string searchTerm)
    {
        const string sql = @"
            SELECT TOP 10
                Id, ObjectClass, DisplayName, Username, Email, FirstName, LastName,
                JobTitle, Department, IsActive
            FROM Objects
            WHERE ObjectClass = 'user'
              AND (
                  LOWER(DisplayName) LIKE @SearchPattern
                  OR LOWER(Username) LIKE @SearchPattern
                  OR LOWER(Email) LIKE @SearchPattern
                  OR LOWER(FirstName) LIKE @SearchPattern
                  OR LOWER(LastName) LIKE @SearchPattern
              )";

        await using var connection = new SqlConnection(_connectionString);
        var users = (await connection.QueryAsync<IdentityObject>(sql, new { SearchPattern = $"%{searchTerm.ToLower()}%" })).ToList();

        if (!users.Any())
        {
            return new ChatResponse
            {
                Message = $"No users found matching '{searchTerm}'.",
                CardResults = null
            };
        }

        var cards = users.Select(u => new CardResult
        {
            Id = u.Id.ToString(),
            Type = "User",
            Title = u.DisplayName ?? u.Username ?? "Unknown",
            DisplayName = u.DisplayName ?? "",
            Subtitle = u.JobTitle ?? u.Department ?? "",
            Email = u.Email,
            Status = u.IsActive ? "Active" : "Disabled",
            IsEnabled = u.IsActive,
            Icon = "person",
            Properties = new Dictionary<string, string>
            {
                { "Username", u.Username ?? "" },
                { "Email", u.Email ?? "" },
                { "Department", u.Department ?? "" },
                { "Title", u.JobTitle ?? "" }
            }
        }).ToList();

        return new ChatResponse
        {
            Message = $"Found {users.Count} user(s) matching '{searchTerm}':",
            CardResults = cards
        };
    }

    private async Task<ChatResponse> ResetPasswordAsync(string searchTerm)
    {
        var user = await FindSingleUserAsync(searchTerm);
        if (user == null)
        {
            return new ChatResponse
            {
                Message = $"Could not find a unique user matching '{searchTerm}'. Please be more specific.",
                CardResults = null
            };
        }

        if (_directoryWriteService == null)
        {
            return new ChatResponse
            {
                Message = "Password reset is not available. Directory write service is not configured.",
                CardResults = null
            };
        }

        // Generate a random password
        var newPassword = GenerateSecurePassword();

        var success = await _directoryWriteService.ResetPasswordAsync(user.Id, newPassword, mustChangeAtNextLogon: true);

        if (success)
        {
            return new ChatResponse
            {
                Message = $"✅ Password reset for **{user.DisplayName ?? user.Username}**\n\n" +
                         $"New password: `{newPassword}`\n\n" +
                         $"_User must change password at next logon._",
                CardResults = new List<CardResult>
                {
                    new CardResult
                    {
                        Id = user.Id.ToString(),
                        Type = "User",
                        Title = user.DisplayName ?? user.Username ?? "Unknown",
                        DisplayName = user.DisplayName ?? "",
                        Status = "Password Reset",
                        Icon = "key",
                        Properties = new Dictionary<string, string>
                        {
                            { "New Password", newPassword },
                            { "Must Change", "Yes" }
                        }
                    }
                }
            };
        }

        return new ChatResponse
        {
            Message = $"❌ Failed to reset password for {user.DisplayName ?? user.Username}. Check the audit log for details.",
            CardResults = null
        };
    }

    private async Task<ChatResponse> DisableUserAsync(string searchTerm)
    {
        var user = await FindSingleUserAsync(searchTerm);
        if (user == null)
        {
            return new ChatResponse
            {
                Message = $"Could not find a unique user matching '{searchTerm}'. Please be more specific.",
                CardResults = null
            };
        }

        if (_writeBackService == null && _directoryWriteService == null)
        {
            return new ChatResponse
            {
                Message = "Account management is not available. Directory write service is not configured.",
                CardResults = null
            };
        }

        bool success;
        if (_writeBackService != null)
        {
            var result = await _writeBackService.SetObjectEnabledAsync(user.Id, false, "ChatBot", WriteBackCallerContext.System("ChatBot"));
            success = result.Success;
        }
        else
        {
            success = await _directoryWriteService!.DisableUserAsync(user.Id);
        }

        if (success)
        {
            return new ChatResponse
            {
                Message = $"✅ Account **{user.DisplayName ?? user.Username}** has been disabled.",
                CardResults = null
            };
        }

        return new ChatResponse
        {
            Message = $"❌ Failed to disable {user.DisplayName ?? user.Username}. Check the audit log for details.",
            CardResults = null
        };
    }

    private async Task<ChatResponse> EnableUserAsync(string searchTerm)
    {
        var user = await FindSingleUserAsync(searchTerm);
        if (user == null)
        {
            return new ChatResponse
            {
                Message = $"Could not find a unique user matching '{searchTerm}'. Please be more specific.",
                CardResults = null
            };
        }

        if (_writeBackService == null && _directoryWriteService == null)
        {
            return new ChatResponse
            {
                Message = "Account management is not available. Directory write service is not configured.",
                CardResults = null
            };
        }

        bool success;
        if (_writeBackService != null)
        {
            var result = await _writeBackService.SetObjectEnabledAsync(user.Id, true, "ChatBot", WriteBackCallerContext.System("ChatBot"));
            success = result.Success;
        }
        else
        {
            success = await _directoryWriteService!.EnableUserAsync(user.Id);
        }

        if (success)
        {
            return new ChatResponse
            {
                Message = $"✅ Account **{user.DisplayName ?? user.Username}** has been enabled.",
                CardResults = null
            };
        }

        return new ChatResponse
        {
            Message = $"❌ Failed to enable {user.DisplayName ?? user.Username}. Check the audit log for details.",
            CardResults = null
        };
    }

    private async Task<ChatResponse> AddUserToGroupAsync(string userSearch, string groupSearch)
    {
        var user = await FindSingleUserAsync(userSearch);
        if (user == null)
        {
            return new ChatResponse
            {
                Message = $"Could not find a unique user matching '{userSearch}'.",
                CardResults = null
            };
        }

        var group = await FindSingleGroupAsync(groupSearch);
        if (group == null)
        {
            return new ChatResponse
            {
                Message = $"Could not find a unique group matching '{groupSearch}'.",
                CardResults = null
            };
        }

        if (_directoryWriteService == null)
        {
            return new ChatResponse
            {
                Message = "Group management is not available. Directory write service is not configured.",
                CardResults = null
            };
        }

        var success = await _directoryWriteService.AddGroupMemberAsync(group.Id, user.Id);

        if (success)
        {
            return new ChatResponse
            {
                Message = $"✅ Added **{user.DisplayName ?? user.Username}** to group **{group.DisplayName}**.",
                CardResults = null
            };
        }

        return new ChatResponse
        {
            Message = $"❌ Failed to add {user.DisplayName ?? user.Username} to {group.DisplayName}.",
            CardResults = null
        };
    }

    private async Task<ChatResponse> RemoveUserFromGroupAsync(string userSearch, string groupSearch)
    {
        var user = await FindSingleUserAsync(userSearch);
        if (user == null)
        {
            return new ChatResponse
            {
                Message = $"Could not find a unique user matching '{userSearch}'.",
                CardResults = null
            };
        }

        var group = await FindSingleGroupAsync(groupSearch);
        if (group == null)
        {
            return new ChatResponse
            {
                Message = $"Could not find a unique group matching '{groupSearch}'.",
                CardResults = null
            };
        }

        if (_directoryWriteService == null)
        {
            return new ChatResponse
            {
                Message = "Group management is not available. Directory write service is not configured.",
                CardResults = null
            };
        }

        var success = await _directoryWriteService.RemoveGroupMemberAsync(group.Id, user.Id);

        if (success)
        {
            return new ChatResponse
            {
                Message = $"✅ Removed **{user.DisplayName ?? user.Username}** from group **{group.DisplayName}**.",
                CardResults = null
            };
        }

        return new ChatResponse
        {
            Message = $"❌ Failed to remove {user.DisplayName ?? user.Username} from {group.DisplayName}.",
            CardResults = null
        };
    }

    private async Task<ChatResponse> ListUserGroupsAsync(string searchTerm)
    {
        var user = await FindSingleUserAsync(searchTerm);
        if (user == null)
        {
            return new ChatResponse
            {
                Message = $"Could not find a unique user matching '{searchTerm}'.",
                CardResults = null
            };
        }

        const string sql = @"
            SELECT g.Id, g.ObjectClass, g.DisplayName, g.Username
            FROM ObjectGroupMemberships m
            INNER JOIN Objects g ON m.GroupId = g.Id
            WHERE m.ObjectId = @ObjectId
              AND g.ObjectClass = 'group'";

        await using var connection = new SqlConnection(_connectionString);
        var memberships = (await connection.QueryAsync<IdentityObject>(sql, new { ObjectId = user.Id })).ToList();

        if (!memberships.Any())
        {
            return new ChatResponse
            {
                Message = $"**{user.DisplayName ?? user.Username}** is not a member of any groups.",
                CardResults = null
            };
        }

        var cards = memberships.Select(g => new CardResult
        {
            Id = g.Id.ToString(),
            Type = "Group",
            Title = g.DisplayName ?? g.Username ?? "Unknown Group",
            DisplayName = g.DisplayName ?? "",
            Icon = "people"
        }).ToList();

        return new ChatResponse
        {
            Message = $"**{user.DisplayName ?? user.Username}** is a member of {memberships.Count} group(s):",
            CardResults = cards
        };
    }

    private async Task<ChatResponse> SearchGroupsAsync(string searchTerm)
    {
        const string sql = @"
            SELECT TOP 10
                Id, ObjectClass, DisplayName, Username
            FROM Objects
            WHERE ObjectClass = 'group'
              AND (
                  LOWER(DisplayName) LIKE @SearchPattern
                  OR LOWER(Username) LIKE @SearchPattern
              )";

        await using var connection = new SqlConnection(_connectionString);
        var groups = (await connection.QueryAsync<IdentityObject>(sql, new { SearchPattern = $"%{searchTerm.ToLower()}%" })).ToList();

        if (!groups.Any())
        {
            return new ChatResponse
            {
                Message = $"No groups found matching '{searchTerm}'.",
                CardResults = null
            };
        }

        var cards = groups.Select(g => new CardResult
        {
            Id = g.Id.ToString(),
            Type = "Group",
            Title = g.DisplayName ?? g.Username ?? "Unknown",
            DisplayName = g.DisplayName ?? "",
            Icon = "people"
        }).ToList();

        return new ChatResponse
        {
            Message = $"Found {groups.Count} group(s) matching '{searchTerm}':",
            CardResults = cards
        };
    }

    private async Task<ChatResponse> GetSyncStatusAsync()
    {
        const string sql = @"
            SELECT TOP 5
                r.Id, r.SyncProjectId, r.Status, r.StartedAt, r.CompletedAt,
                p.Name AS ProjectName
            FROM SyncProjectRuns r
            INNER JOIN SyncProjects p ON r.SyncProjectId = p.Id
            ORDER BY r.StartedAt DESC";

        await using var connection = new SqlConnection(_connectionString);
        var recentRuns = (await connection.QueryAsync<dynamic>(sql)).ToList();

        if (!recentRuns.Any())
        {
            return new ChatResponse
            {
                Message = "No sync runs found.",
                CardResults = null
            };
        }

        var runningCount = recentRuns.Count(r => r.Status == "Running");
        var message = runningCount > 0
            ? $"**{runningCount} sync(s) currently running**\n\n"
            : "No syncs currently running.\n\n";

        message += "**Recent Sync Runs:**\n";
        foreach (var item in recentRuns)
        {
            var status = item.Status == "Running" ? "🔄" : (item.Status == "Completed" ? "✅" : "❌");
            message += $"{status} {item.ProjectName ?? "Unknown"} - {item.Status} ({((DateTime)item.StartedAt):g})\n";
        }

        return new ChatResponse
        {
            Message = message,
            CardResults = null
        };
    }

    #endregion

    #region Helper Methods

    private async Task<IdentityObject?> FindSingleUserAsync(string searchTerm)
    {
        const string sql = @"
            SELECT TOP 5
                Id, ObjectClass, DisplayName, Username, Email, FirstName, LastName,
                JobTitle, Department, IsActive
            FROM Objects
            WHERE ObjectClass = 'user'
              AND (
                  LOWER(DisplayName) LIKE @SearchPattern
                  OR LOWER(Username) LIKE @SearchPattern
                  OR LOWER(Email) LIKE @SearchPattern
              )";

        await using var connection = new SqlConnection(_connectionString);
        var users = (await connection.QueryAsync<IdentityObject>(sql, new { SearchPattern = $"%{searchTerm.ToLower()}%" })).ToList();

        // If exact username match, prefer that
        var exactMatch = users.FirstOrDefault(u =>
            u.Username?.Equals(searchTerm, StringComparison.OrdinalIgnoreCase) == true);
        if (exactMatch != null)
            return exactMatch;

        // If only one result, use it
        if (users.Count == 1)
            return users[0];

        // Multiple matches - can't determine which one
        return null;
    }

    private async Task<IdentityObject?> FindSingleGroupAsync(string searchTerm)
    {
        const string sql = @"
            SELECT TOP 5
                Id, ObjectClass, DisplayName, Username
            FROM Objects
            WHERE ObjectClass = 'group'
              AND (
                  LOWER(DisplayName) LIKE @SearchPattern
                  OR LOWER(Username) LIKE @SearchPattern
              )";

        await using var connection = new SqlConnection(_connectionString);
        var groups = (await connection.QueryAsync<IdentityObject>(sql, new { SearchPattern = $"%{searchTerm.ToLower()}%" })).ToList();

        // If exact name match, prefer that
        var exactMatch = groups.FirstOrDefault(g =>
            g.DisplayName?.Equals(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
            g.Username?.Equals(searchTerm, StringComparison.OrdinalIgnoreCase) == true);
        if (exactMatch != null)
            return exactMatch;

        if (groups.Count == 1)
            return groups[0];

        return null;
    }

    private static string GenerateSecurePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%&*";

        var random = new Random();
        var password = new char[12];

        // Ensure at least one of each type
        password[0] = upper[random.Next(upper.Length)];
        password[1] = lower[random.Next(lower.Length)];
        password[2] = digits[random.Next(digits.Length)];
        password[3] = special[random.Next(special.Length)];

        // Fill the rest randomly
        var allChars = upper + lower + digits + special;
        for (int i = 4; i < password.Length; i++)
        {
            password[i] = allChars[random.Next(allChars.Length)];
        }

        // Shuffle
        return new string(password.OrderBy(_ => random.Next()).ToArray());
    }

    #endregion
}
