namespace Carvis.Core.Chat;

public enum ChatRole
{
    System,
    User,
    Assistant,
}

public sealed record ChatMessage(ChatRole Role, string Content);
