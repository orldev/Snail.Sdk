using ModelContextProtocol.Protocol;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>What a test reads from a tool's answer.</summary>
public static class ToolAnswers
{
    extension(CallToolResult answer)
    {
        /// <summary>The text of a tool's answer, which is one text block in every Snail server.</summary>
        /// <exception cref="InvalidOperationException">The answer is not exactly one text block.</exception>
        public string Text()
        {
            ArgumentNullException.ThrowIfNull(answer);

            return answer.Content is [TextContentBlock block] ? block.Text : throw new InvalidOperationException($"expected one text block, got {answer.Content.Count} blocks");
        }
    }
}
