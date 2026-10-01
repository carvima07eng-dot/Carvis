using Carvis.Core.Tools;

namespace Carvis.Core.Chat;

/// <summary>What happens while Carvis answers: text arriving and tools being run.</summary>
public abstract record ChatEvent;

public sealed record TextDelta(string Text) : ChatEvent;

/// <summary>A tool is about to run (or waits for the user's confirmation).</summary>
public sealed record ToolStarted(ToolInvocation Invocation) : ChatEvent;

public sealed record ToolFinished(ToolInvocation Invocation, ToolResult Result) : ChatEvent;

/// <summary>The model finished one step and will continue after the tool results.</summary>
public sealed record StepCompleted(int Step) : ChatEvent;

/// <summary>Reasoning shown folded under the answer when thinking is on.</summary>
public sealed record ThinkingDelta(string Text) : ChatEvent;

/// <summary>Speed of the answer, for the footer.</summary>
public sealed record StatsReported(GenerationStats Stats) : ChatEvent;
