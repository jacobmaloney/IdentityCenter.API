using Dapper;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-backed implementation of <see cref="IChatFeedbackRepository"/>.
/// </summary>
public class ChatFeedbackRepository : DapperRepositoryBase, IChatFeedbackRepository
{
    public ChatFeedbackRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public Task<Guid> SaveFeedbackAsync(string messageId, string? userId, int feedback, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("messageId is required", nameof(messageId));
        if (feedback != 1 && feedback != -1)
            throw new ArgumentOutOfRangeException(nameof(feedback), "feedback must be +1 or -1");

        return ExecuteAsync(async conn =>
        {
            var id = Guid.NewGuid();
            await conn.ExecuteAsync(new CommandDefinition(
                @"INSERT INTO ChatFeedback (Id, MessageId, UserId, Feedback, CreatedAt)
                  VALUES (@Id, @MessageId, @UserId, @Feedback, @CreatedAt);",
                new
                {
                    Id = id,
                    MessageId = messageId,
                    UserId = userId,
                    Feedback = feedback,
                    CreatedAt = DateTime.UtcNow,
                },
                cancellationToken: ct));
            return id;
        }, ct);
    }

    public Task<int?> GetLatestFeedbackAsync(string messageId, string? userId, CancellationToken ct = default)
    {
        return ExecuteAsync(async conn =>
        {
            var sql = userId is null
                ? @"SELECT TOP 1 Feedback FROM ChatFeedback
                    WHERE MessageId = @MessageId AND UserId IS NULL
                    ORDER BY CreatedAt DESC"
                : @"SELECT TOP 1 Feedback FROM ChatFeedback
                    WHERE MessageId = @MessageId AND UserId = @UserId
                    ORDER BY CreatedAt DESC";
            return await conn.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(
                sql,
                new { MessageId = messageId, UserId = userId },
                cancellationToken: ct));
        }, ct);
    }
}
