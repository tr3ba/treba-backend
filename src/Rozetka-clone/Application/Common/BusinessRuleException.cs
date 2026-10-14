namespace Application.Common;

/// <summary>An expected conflict with the current state of the application.</summary>
public sealed class BusinessRuleException(
    string message
) : InvalidOperationException(message);
