using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StoryDataCollector;

internal sealed class StoryEventScriptSummary
{
    internal List<string> Participants { get; } = new();

    internal List<string> DialogueHighlights { get; } = new();

    internal List<string> ActionCues { get; } = new();

    internal bool PlayerParticipated { get; set; }
}

internal static class StoryEventScriptParser
{
    internal const int MaxParticipants = 16;
    internal const int MaxDialogueHighlights = 12;
    internal const int MaxActionCues = 12;
    internal const int MaxPlayerChoices = 8;
    private const int MaxDialogueLength = 240;
    private const int MaxActionLength = 160;
    internal const int MaxObservedCommands = 512;
    private const int MaxCommandLength = 2048;
    private const int MaxCommandArguments = 128;

    private static readonly HashSet<string> ActorCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "animate",
        "emote",
        "faceDirection",
        "jump",
        "lookAround",
        "move",
        "positionOffset",
        "removeCharacter",
        "shake",
        "showFrame",
        "speak",
        "stopAnimation"
    };

    private static readonly HashSet<string> ActionCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "addObject",
        "addTemporaryActor",
        "animate",
        "changeLocation",
        "emote",
        "faceDirection",
        "farmer",
        "globalFade",
        "jump",
        "move",
        "music",
        "playSound",
        "removeObject",
        "shake",
        "showFrame",
        "viewport"
    };

    internal static StoryEventScriptSummary Extract(IEnumerable<string>? commands)
    {
        string[] script = commands?
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Take(MaxObservedCommands)
            .Select(LimitCommand)
            .ToArray()
            ?? Array.Empty<string>();
        StoryEventScriptSummary summary = CreateInitial(script);
        foreach (string command in script)
            ObserveCommand(summary, Tokenize(command));
        return summary;
    }

    internal static StoryEventScriptSummary CreateInitial(IReadOnlyList<string>? commands)
    {
        StoryEventScriptSummary summary = new();
        if (commands is not null && commands.Count > 2)
            AddInitialParticipants(LimitCommand(commands[2]), summary);
        summary.PlayerParticipated = summary.Participants.Contains("Player", StringComparer.Ordinal);
        return summary;
    }

    internal static void ObserveCommand(StoryEventScriptSummary summary, IReadOnlyList<string>? rawTokens)
    {
        if (rawTokens is null || rawTokens.Count == 0)
            return;
        List<string> tokens = rawTokens
            .Take(MaxCommandArguments)
            .Select(token => LimitCommand(token ?? ""))
            .ToList();
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(tokens[0]))
            return;

        string commandName = tokens[0];
        if (ActorCommands.Contains(commandName) && tokens.Count > 1)
            AddParticipant(summary, tokens[1]);
        if (commandName.Equals("addTemporaryActor", StringComparison.OrdinalIgnoreCase) && tokens.Count > 1)
            AddParticipant(summary, tokens[1]);

        if (commandName.Equals("speak", StringComparison.OrdinalIgnoreCase) && tokens.Count > 2)
        {
            string? actor = NormalizeParticipant(tokens[1]);
            string dialogue = CleanText(string.Join(" ", tokens.Skip(2)), MaxDialogueLength);
            if (!string.IsNullOrWhiteSpace(dialogue))
                AddUnique(summary.DialogueHighlights, $"{actor ?? tokens[1]}：{dialogue}", MaxDialogueHighlights);
        }
        else if (commandName.Equals("message", StringComparison.OrdinalIgnoreCase) && tokens.Count > 1)
        {
            string message = CleanText(string.Join(" ", tokens.Skip(1)), MaxDialogueLength);
            if (!string.IsNullOrWhiteSpace(message))
                AddUnique(summary.DialogueHighlights, message, MaxDialogueHighlights);
        }
        else if (commandName.Equals("question", StringComparison.OrdinalIgnoreCase)
            || commandName.Equals("quickQuestion", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = ExtractQuestionParts(tokens);
            if (parts.Length > 0)
            {
                string question = CleanText(parts[0], MaxDialogueLength);
                string[] options = parts.Skip(1)
                    .Select(option => CleanText(option, MaxDialogueLength))
                    .Where(option => !string.IsNullOrWhiteSpace(option))
                    .ToArray();
                string line = options.Length == 0
                    ? $"问题：{question}"
                    : $"问题：{question}（选项：{string.Join(" / ", options)}）";
                AddUnique(summary.DialogueHighlights, line, MaxDialogueHighlights);
            }
        }
        else if (ActionCommands.Contains(commandName))
        {
            string? cue = DescribeAction(commandName, tokens);
            if (!string.IsNullOrWhiteSpace(cue))
                AddUnique(summary.ActionCues, CleanText(cue, MaxActionLength), MaxActionCues);
        }

        summary.PlayerParticipated = summary.Participants.Contains("Player", StringComparer.Ordinal);
    }

    internal static string? ExtractQuestionText(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        string[] parts = ExtractQuestionParts(Tokenize(LimitCommand(command)));
        return parts.Length > 0 ? CleanText(parts[0], MaxDialogueLength) : null;
    }

    internal static string? ExtractSelectedChoice(string? command, int answerChoice)
    {
        if (string.IsNullOrWhiteSpace(command) || answerChoice < 0)
            return null;

        string[] parts = ExtractQuestionParts(Tokenize(LimitCommand(command)));
        int optionIndex = answerChoice + 1;
        return optionIndex < parts.Length
            ? CleanText(parts[optionIndex], MaxDialogueLength)
            : null;
    }

    private static void AddInitialParticipants(string command, StoryEventScriptSummary summary)
    {
        List<string> tokens = Tokenize(command);
        for (int index = 0; index + 3 < tokens.Count; index += 4)
            AddParticipant(summary, tokens[index]);
    }

    private static void AddParticipant(StoryEventScriptSummary summary, string candidate)
    {
        string? participant = NormalizeParticipant(candidate);
        if (participant is not null)
            AddUnique(summary.Participants, participant, MaxParticipants);
    }

    internal static string? NormalizeParticipant(string candidate)
    {
        string value = candidate.Trim().Trim('"');
        if (value.StartsWith("farmer", StringComparison.OrdinalIgnoreCase))
            return "Player";
        if (value.Length == 0 || value.All(character => char.IsDigit(character) || character == '-'))
            return null;
        return CleanText(value, 80);
    }

    private static string? DescribeAction(string commandName, IReadOnlyList<string> tokens)
    {
        string actor = tokens.Count > 1 ? NormalizeParticipant(tokens[1]) ?? tokens[1] : "现场";
        return commandName.ToLowerInvariant() switch
        {
            "animate" => $"{actor} 执行动画",
            "emote" => $"{actor} 表现情绪{FormatArgument(tokens, 2)}",
            "facedirection" => $"{actor} 转向{FormatArgument(tokens, 2)}",
            "farmer" => $"玩家动作：{string.Join(" ", tokens.Skip(1))}",
            "jump" => $"{actor} 跳起",
            "move" => $"{actor} 移动",
            "playsound" => $"现场声音：{string.Join(" ", tokens.Skip(1))}",
            "music" => $"现场音乐：{string.Join(" ", tokens.Skip(1))}",
            "showframe" => $"{actor} 切换动作{FormatArgument(tokens, 2)}",
            "shake" => $"{actor} 震动",
            "addtemporaryactor" => $"{actor} 进入场景",
            "changelocation" => $"场景切换至{FormatArgument(tokens, 1)}",
            "addobject" or "removeobject" => $"场景物件变化：{string.Join(" ", tokens)}",
            "globalfade" or "viewport" => null,
            _ => string.Join(" ", tokens)
        };
    }

    private static string FormatArgument(IReadOnlyList<string> tokens, int index)
    {
        return index < tokens.Count ? $"（{tokens[index]}）" : "";
    }

    private static string[] ExtractQuestionParts(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
            return Array.Empty<string>();

        int payloadIndex = tokens[0].Equals("question", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        if (payloadIndex >= tokens.Count)
            return Array.Empty<string>();
        string payload = string.Join(" ", tokens.Skip(payloadIndex));
        int branch = payload.IndexOf("(break)", StringComparison.Ordinal);
        if (branch >= 0)
            payload = payload[..branch];
        payload = TrimOuterQuotes(payload);
        return payload.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static List<string> Tokenize(string command)
    {
        List<string> result = new();
        StringBuilder current = new();
        bool quoted = false;
        bool escaped = false;
        foreach (char character in command)
        {
            if (escaped)
            {
                current.Append(character);
                escaped = false;
                continue;
            }
            if (character == '\\')
            {
                escaped = true;
                continue;
            }
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(character);
        }
        if (escaped)
            current.Append('\\');
        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }

    private static string TrimOuterQuotes(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private static string LimitCommand(string command)
    {
        return command.Length <= MaxCommandLength ? command : command[..MaxCommandLength];
    }

    private static string CleanText(string value, int maximumLength)
    {
        string cleaned = TrimOuterQuotes(value)
            .Replace("#$b#", " ", StringComparison.Ordinal)
            .Replace("$b", " ", StringComparison.Ordinal);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length <= maximumLength
            ? cleaned
            : cleaned[..maximumLength].TrimEnd() + "…";
    }

    private static void AddUnique(ICollection<string> values, string value, int maximumCount)
    {
        if (values.Count >= maximumCount || values.Contains(value, StringComparer.Ordinal))
            return;
        values.Add(value);
    }
}
