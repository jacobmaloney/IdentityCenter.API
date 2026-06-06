using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataAccessLibrary.Services.Chat
{
    /// <summary>
    /// Interface for ChatBot service
    /// </summary>
    public interface IChatBotService
    {
        /// <summary>
        /// Sends a message to the chatbot and gets a response
        /// </summary>
        Task<ChatResponse> SendMessageAsync(string message, string userId);
    }

    /// <summary>
    /// Response from the chatbot
    /// </summary>
    public class ChatResponse
    {
        public string Message { get; set; } = string.Empty;
        public List<CardResult>? CardResults { get; set; }
    }

    /// <summary>
    /// Represents a result card to display in chat
    /// </summary>
    public class CardResult
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // User, Group, Computer
        public string Title { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string? Email { get; set; }
        public string? Status { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new();
        public Dictionary<string, string> AdditionalInfo { get; set; } = new();
    }
}
