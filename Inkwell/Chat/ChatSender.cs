using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Inkwell.Chat;

/// <summary>
/// Sends one line to the game exactly as if it had been typed in the chat box and Enter pressed.
/// Only ever called from a button click: one click, one message.
///
/// Everything here touches game memory, so it must run on the framework (game) thread.
/// Call it through Plugin.Framework.RunOnFrameworkThread.
/// </summary>
public static unsafe class ChatSender
{
    public const int MaxBytes = 500;

    // UppercaseLetters | LowercaseLetters | Numbers | SpecialCharacters | CharacterList |
    // OtherCharacters | Payloads | Unknown9. Written as a number so it still compiles if
    // ClientStructs renames the enum members.
    private const int AllowedEntityFlags = 0x17F;

    /// <summary>Checks and sends. Returns null on success, or a message explaining why it didn't send.</summary>
    public static string? TrySend(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Nothing to send.";

        if (message.Contains('\n') || message.Contains('\r'))
            return "The message contains a line break.";

        var bytes = Encoding.UTF8.GetByteCount(message);
        if (bytes > MaxBytes)
            return $"The message is {bytes} bytes; the game's limit is {MaxBytes}.";

        // Run the text through the game's own filter. If anything gets removed, the game wouldn't
        // accept it from the real chat box either, so refuse rather than send something odd.
        if (Sanitize(message) != message)
            return "The message contains characters the game chat won't accept. Copy it instead.";

        var str = Utf8String.FromString(message);
        try
        {
            UIModule.Instance()->ProcessChatBoxEntry(str);
        }
        finally
        {
            str->Dtor(true);
        }
        return null;
    }

    private static string Sanitize(string text)
    {
        var str = Utf8String.FromString(text);
        try
        {
            str->SanitizeString((AllowedEntities)AllowedEntityFlags, null);
            return str->ToString();
        }
        finally
        {
            str->Dtor(true);
        }
    }
}
