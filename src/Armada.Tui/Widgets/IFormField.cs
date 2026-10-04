namespace Armada.Tui.Widgets
{
    /// <summary>
    /// A field hosted by <see cref="FormView"/>: exposes a comparable value for dirty tracking and validation.
    /// </summary>
    public interface IFormField
    {
        /// <summary>
        /// Current value boxed for comparison (strings, numbers, booleans, lists rendered as strings).
        /// </summary>
        object? FieldValue { get; }

        /// <summary>
        /// Last validation error (English), or null.
        /// </summary>
        string? FieldError { get; }

        /// <summary>
        /// Validate the current value.
        /// </summary>
        /// <returns>True when valid.</returns>
        bool ValidateField();
    }
}
