namespace Armada.Runtimes.Tools.Arguments
{
    /// <summary>
    /// Implemented by every strongly typed built-in tool argument class. Type checking is done by the JSON
    /// deserializer; this contract adds the checks the deserializer cannot express (required values present,
    /// array elements non-null).
    /// </summary>
    public interface IToolArguments
    {
        /// <summary>
        /// Validates the deserialized arguments.
        /// </summary>
        /// <returns>Null when the arguments are valid; otherwise a message describing the first problem found.</returns>
        string? Validate();
    }
}
