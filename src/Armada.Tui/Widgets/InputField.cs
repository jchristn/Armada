namespace Armada.Tui.Widgets
{
    /// <summary>
    /// A <see cref="TextInput"/> usable in a <see cref="FormView"/> (dirty tracking and validation). Not thread-safe.
    /// </summary>
    public class InputField : TextInput, IFormField
    {
        #region Public-Members

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return Value; }
        }

        /// <inheritdoc />
        public string? FieldError
        {
            get { return Error; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool ValidateField()
        {
            return Validate();
        }

        #endregion
    }
}
