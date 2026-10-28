namespace Radivum.Azure;

/// <summary>
/// Thrown when a collection scope cannot be collected as requested.
/// </summary>
/// <remarks>
/// This is an operator-facing refusal, not a defect: the scope names more subscriptions than one analysis covers,
/// mixes a resource group with several subscriptions, repeats an identifier, belongs to another tenant, or leaves
/// nothing readable. It is a distinct type so a caller can tell that class of failure from a transport or service
/// failure and report the reason the operator can act on — the connected host answers it as an invalid request
/// rather than as a failed collection.
/// </remarks>
public sealed class CollectionScopeException : Exception
{
    /// <summary>
    /// Creates the exception with the reason to report.
    /// </summary>
    /// <param name="message">Operator-readable reason the scope cannot be collected.</param>
    public CollectionScopeException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with the reason to report and the underlying cause.
    /// </summary>
    /// <param name="message">Operator-readable reason the scope cannot be collected.</param>
    /// <param name="innerException">Cause of the refusal.</param>
    public CollectionScopeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
