namespace Armada.Tui.Services
{
    /// <summary>
    /// Outcome of a sign-in attempt.
    /// </summary>
    public class SignInResult
    {
        #region Public-Members

        /// <summary>
        /// True when signed in.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// English error message (the dashboard's text), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SignInResult()
        {
        }

        /// <summary>
        /// A failure.
        /// </summary>
        /// <param name="error">English error.</param>
        /// <returns>Result.</returns>
        public static SignInResult Fail(string error)
        {
            SignInResult r = new SignInResult();
            r.Error = error;
            return r;
        }

        /// <summary>
        /// A success.
        /// </summary>
        /// <returns>Result.</returns>
        public static SignInResult Ok()
        {
            SignInResult r = new SignInResult();
            r.Success = true;
            return r;
        }

        #endregion
    }
}
