namespace Armada.Tui.Screens
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;

    /// <summary>
    /// The login screen, matching the dashboard's LoginFlow (W1.14): Email Login (email, tenant lookup, tenant picker
    /// when several match, password) and API Key Login (key or bearer token validated with <c>whoami</c>), plus the
    /// server profile picker, language and theme pickers, version, GitHub link, and the default-credentials hint.
    /// Passwords and keys are masked (<c>Ctrl+R</c> reveals). Errors use the dashboard's wording. Not thread-safe.
    /// </summary>
    public class LoginView : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Login mode tabs (email, apikey).
        /// </summary>
        public TabStrip Modes { get; } = new TabStrip();

        /// <summary>
        /// Email field.
        /// </summary>
        public InputField Email { get; } = new InputField();

        /// <summary>
        /// Tenant picker.
        /// </summary>
        public SelectField<string> Tenant { get; } = new SelectField<string>();

        /// <summary>
        /// Password field.
        /// </summary>
        public InputField Password { get; } = new InputField();

        /// <summary>
        /// API key field.
        /// </summary>
        public InputField ApiKey { get; } = new InputField();

        /// <summary>
        /// Server profile picker.
        /// </summary>
        public SelectField<string> Server { get; } = new SelectField<string>();

        /// <summary>
        /// Language picker.
        /// </summary>
        public SelectField<string> Language { get; } = new SelectField<string>();

        /// <summary>
        /// Theme picker.
        /// </summary>
        public SelectField<ThemeModeEnum> ThemePicker { get; } = new SelectField<ThemeModeEnum>();

        /// <summary>
        /// Current email step.
        /// </summary>
        public LoginStepEnum Step { get; private set; } = LoginStepEnum.Email;

        /// <summary>
        /// API key mode is active.
        /// </summary>
        public bool ApiKeyMode
        {
            get { return Modes.SelectedKey == "apikey"; }
        }

        /// <summary>
        /// English error shown above the form, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// English notice (for example the session-expired reason), or null.
        /// </summary>
        public string? Notice { get; set; } = null;

        /// <summary>
        /// True while a request is in flight.
        /// </summary>
        public bool Busy { get; private set; } = false;

        /// <summary>
        /// Tenants from the last lookup.
        /// </summary>
        public List<TenantListEntry> Tenants { get; private set; } = new List<TenantListEntry>();

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;
        private readonly ButtonRow _Buttons = new ButtonRow();
        private readonly Button _Primary;
        private readonly Button _Back;
        private TenantListEntry? _SelectedTenant = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Context.</param>
        public LoginView(TuiContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            Localizer = context.Loc;
            ApplyTheme(context.Theme.Current);
            Modes.Add("email", "Email Login");
            Modes.Add("apikey", "API Key Login");
            Modes.SelectKey(context.Session.Profile.AuthMethod == "apikey" ? "apikey" : "email");
            Modes.SelectedChanged += (s, e) => { Error = null; Rebuild(); };

            Email.Placeholder = "you@company.com";
            Email.Value = context.Session.Profile.LastUser ?? "";
            Email.Submitted += (s, e) => SubmitEmail();
            Password.Masked = true;
            Password.Placeholder = "Password";
            Password.Submitted += (s, e) => SubmitPassword();
            ApiKey.Masked = true;
            ApiKey.Placeholder = "Paste your API key";
            ApiKey.Submitted += (s, e) => SubmitApiKey();
            Tenant.PickerTitle = "Tenant";
            Tenant.Placeholder = "Select a tenant...";
            Tenant.ModalHost = context.Modals;
            Tenant.ValueChanged += (s, e) => _SelectedTenant = Tenants.FirstOrDefault(t => t.Id == e.NewValue);

            Server.PickerTitle = "Server";
            Server.ModalHost = context.Modals;
            Server.ValueChanged += (s, e) => OnServerChosen(e.NewValue);
            Language.PickerTitle = "Language";
            Language.ModalHost = context.Modals;
            Language.ValueChanged += (s, e) => SetLanguage(e.NewValue);
            ThemePicker.PickerTitle = "Theme";
            ThemePicker.ModalHost = context.Modals;
            ThemePicker.Options = new List<SelectOption<ThemeModeEnum>>
            {
                new SelectOption<ThemeModeEnum>(ThemeModeEnum.Auto, "Auto"),
                new SelectOption<ThemeModeEnum>(ThemeModeEnum.Dark, "Dark"),
                new SelectOption<ThemeModeEnum>(ThemeModeEnum.Light, "Light"),
                new SelectOption<ThemeModeEnum>(ThemeModeEnum.HighContrast, "High contrast")
            };
            ThemePicker.SetValue(context.Prefs.Current.Theme);
            ThemePicker.ValueChanged += (s, e) =>
            {
                _Context.Theme.Apply(e.NewValue);
                _Context.Prefs.Current.Theme = e.NewValue;
                _Context.Prefs.Save();
            };

            _Primary = new Button("Continue");
            _Primary.Pressed += (s, e) => SubmitCurrent();
            _Back = new Button("Back", Back);
            _Buttons.Add(_Primary);
            _Buttons.Add(_Back);
            RefreshPickers();
            Rebuild();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reload the server and language options (after the catalog loads or profiles change).
        /// </summary>
        public void RefreshPickers()
        {
            Server.Options = _Context.Prefs.Current.Profiles
                .Select(p => new SelectOption<string>(p.Name, p.Name, p.Url))
                .Concat(new[] { new SelectOption<string>("__add__", T("+ Add server..."), "") })
                .ToList();
            Server.SetValue(_Context.Session.Profile.Name);
            Language.Options = _Context.Loc.SupportedLocales.Select(l => new SelectOption<string>(l.Code, l.NativeLabel, l.Label)).ToList();
            Language.SetValue(_Context.Loc.Locale);
        }

        /// <summary>
        /// Run the current step's primary action (Continue, Sign In, or Connect).
        /// </summary>
        public void SubmitCurrent()
        {
            if (ApiKeyMode) SubmitApiKey();
            else if (Step == LoginStepEnum.Email) SubmitEmail();
            else if (Step == LoginStepEnum.Tenant) SubmitTenant();
            else SubmitPassword();
        }

        /// <summary>
        /// Look up tenants for the email.
        /// </summary>
        public void SubmitEmail()
        {
            if (Busy) return;
            string email = Email.Value.Trim();
            if (email.Length == 0 || !email.Contains('@'))
            {
                Error = "Enter your email address.";
                return;
            }

            Error = null;
            Busy = true;
            _ = Task.Run(async () =>
            {
                List<TenantListEntry>? tenants = null;
                try { tenants = await _Context.Session.LookupTenantsAsync(email).ConfigureAwait(false); }
                catch (ArmadaApiException) { tenants = null; }
                _Context.Dispatcher.Post(() =>
                {
                    Busy = false;
                    if (tenants == null)
                    {
                        Error = "Failed to look up tenants.";
                        return;
                    }

                    Tenants = tenants;
                    if (tenants.Count == 0)
                    {
                        Error = "No tenants found for this email.";
                    }
                    else if (tenants.Count == 1)
                    {
                        _SelectedTenant = tenants[0];
                        GoTo(LoginStepEnum.Password);
                    }
                    else
                    {
                        Tenant.Options = tenants.Select(t => new SelectOption<string>(t.Id, t.Name, t.Id)).ToList();
                        Tenant.SetValue(_Context.Session.Profile.LastTenantId);
                        _SelectedTenant = tenants.FirstOrDefault(t => t.Id == _Context.Session.Profile.LastTenantId);
                        GoTo(LoginStepEnum.Tenant);
                    }
                });
            });
        }

        /// <summary>
        /// Continue from the tenant picker.
        /// </summary>
        public void SubmitTenant()
        {
            if (_SelectedTenant == null)
            {
                Error = "Please select a tenant.";
                return;
            }

            Error = null;
            GoTo(LoginStepEnum.Password);
        }

        /// <summary>
        /// Sign in with the password.
        /// </summary>
        public void SubmitPassword()
        {
            if (Busy || _SelectedTenant == null) return;
            string email = Email.Value.Trim();
            string tenantId = _SelectedTenant.Id;
            string password = Password.Value;
            Error = null;
            Busy = true;
            _ = Task.Run(async () =>
            {
                SignInResult result = await _Context.Session.SignInWithPasswordAsync(email, tenantId, password).ConfigureAwait(false);
                _Context.Dispatcher.Post(() =>
                {
                    Busy = false;
                    if (!result.Success)
                    {
                        Error = result.Error ?? "Authentication failed.";
                        Password.Value = "";
                    }
                });
            });
        }

        /// <summary>
        /// Sign in with the API key.
        /// </summary>
        public void SubmitApiKey()
        {
            if (Busy) return;
            string key = ApiKey.Value.Trim();
            if (key.Length == 0)
            {
                Error = "API key authentication failed.";
                return;
            }

            Error = null;
            Busy = true;
            _Context.Session.UseApiKeyMethod();
            _ = Task.Run(async () =>
            {
                SignInResult result = await _Context.Session.SignInWithTokenAsync(key, true).ConfigureAwait(false);
                _Context.Dispatcher.Post(() =>
                {
                    Busy = false;
                    if (!result.Success) Error = result.Error ?? "API key authentication failed.";
                });
            });
        }

        /// <summary>
        /// Back to the previous step.
        /// </summary>
        public void Back()
        {
            Error = null;
            if (Step == LoginStepEnum.Password && Tenants.Count > 1) GoTo(LoginStepEnum.Tenant);
            else GoTo(LoginStepEnum.Email);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.F2)
            {
                Modes.SelectedIndex = ApiKeyMode ? 0 : 1;
                Error = null;
                Rebuild();
                return true;
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Down) return Scope.Move(true);
            if (key.Code == KeyCode.Up) return Scope.Move(false);
            if (key.Code == KeyCode.Escape && !ApiKeyMode && Step != LoginStepEnum.Email)
            {
                Back();
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            int cardWidth = Math.Min(64, width - 4);
            int left = Math.Max(0, (width - cardWidth) / 2);
            int y = Math.Max(0, height / 2 - 11);
            Center(surface, ref y, "A R M A D A", Theme.Accent, width);
            Center(surface, ref y, T("Multi-agent orchestration") + "  v" + Armada.Core.Constants.ProductVersion, Theme.Muted, width);
            y++;
            Label(surface, left, y, T("Server"));
            Scope.RenderChild(surface, Server, new Rect(left + 14, y++, cardWidth - 14, 1));
            SurfaceText.Draw(surface, left + 14, y++, _Context.Session.Profile.Url, Theme.Muted, cardWidth - 14);
            y++;
            Scope.RenderChild(surface, Modes, new Rect(left, y, cardWidth, 1));
            y++;
            y++;
            string? message = Error ?? Notice;
            if (message != null)
            {
                int row = y;
                foreach (string line in TextCells.Wrap((Error != null ? "! " : "") + T(message), cardWidth).Take(2))
                {
                    SurfaceText.Draw(surface, left, row++, line, Error != null ? Theme.Error : Theme.Warning, cardWidth);
                }
            }

            y += 2;
            if (ApiKeyMode)
            {
                SurfaceText.Draw(surface, left, y++, T("API Key / Bearer Token"), Theme.Muted, cardWidth);
                Scope.RenderChild(surface, ApiKey, new Rect(left + 14, y++, cardWidth - 14, 1));
            }
            else if (Step == LoginStepEnum.Email)
            {
                Label(surface, left, y, T("Email"));
                Scope.RenderChild(surface, Email, new Rect(left + 14, y++, cardWidth - 14, 1));
            }
            else if (Step == LoginStepEnum.Tenant)
            {
                Label(surface, left, y, T("Tenant"));
                Scope.RenderChild(surface, Tenant, new Rect(left + 14, y++, cardWidth - 14, 1));
            }
            else
            {
                Dictionary<string, object?> args = LocalizationArgs.Of("email", Email.Value.Trim(), "tenant", _SelectedTenant?.Name ?? "");
                SurfaceText.Draw(surface, left, y++, _Context.Loc.T("Signing in as {{email}} to {{tenant}}", args), Theme.Muted, cardWidth);
                y++;
                Label(surface, left, y, T("Password"));
                Scope.RenderChild(surface, Password, new Rect(left + 14, y++, cardWidth - 14, 1));
            }

            if (ApiKeyMode || Step == LoginStepEnum.Password) SurfaceText.Draw(surface, left + 14, y, "Ctrl+R " + T(ApiKeyMode ? "Show API key" : "Show password"), Theme.Muted, cardWidth - 14);
            y += 2;
            _Primary.Label = Busy ? (ApiKeyMode ? "Connecting..." : Step == LoginStepEnum.Email ? "Looking up..." : "Signing in...") : (ApiKeyMode ? "Connect" : Step == LoginStepEnum.Password ? "Sign In" : "Continue");
            _Primary.Enabled = !Busy;
            _Back.Visible = !ApiKeyMode && Step != LoginStepEnum.Email;
            _Back.Label = "Back";
            Scope.RenderChild(surface, _Buttons, new Rect(left + 14, y++, cardWidth - 14, 1));
            y++;
            Label(surface, left, y, T("Language"));
            Scope.RenderChild(surface, Language, new Rect(left + 14, y, Math.Max(10, (cardWidth - 14) / 2 - 1), 1));
            int themeLeft = left + 14 + (cardWidth - 14) / 2 + 1;
            Scope.RenderChild(surface, ThemePicker, new Rect(themeLeft, y++, Math.Max(8, cardWidth - (themeLeft - left)), 1));
            y++;
            Center(surface, ref y, T("Default credentials") + ": admin@armada / password", Theme.Muted, width);
            Center(surface, ref y, "github.com/jchristn/Armada", Theme.Link, width);
            Center(surface, ref y, "Tab " + T("Next field") + "   F2 " + T("Switch login mode") + "   Ctrl+Q " + T("Quit"), Theme.Muted, width);
        }

        #endregion

        #region Private-Methods

        private void GoTo(LoginStepEnum step)
        {
            Step = step;
            Rebuild();
        }

        private void Rebuild()
        {
            bool active = Scope.IsActive;
            if (active) Scope.SetActive(false);
            Scope.Clear();
            ArmadaWidget primary;
            if (ApiKeyMode) primary = ApiKey;
            else if (Step == LoginStepEnum.Email) primary = Email;
            else if (Step == LoginStepEnum.Tenant) primary = Tenant;
            else primary = Password;
            AddChild(primary);
            AddChild(_Buttons);
            AddChild(Modes);
            AddChild(Server);
            AddChild(Language);
            AddChild(ThemePicker);
            Scope.Wrap = true;
            Scope.Focus(primary);
            if (active) Scope.SetActive(true);
        }

        private void OnServerChosen(string? name)
        {
            if (name == "__add__")
            {
                PromptModal prompt = new PromptModal(T("Server URL (for example http://127.0.0.1:7890)"), "http://");
                _Context.Modals.Show(prompt, result =>
                {
                    string? url = result as string;
                    if (String.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url!.Trim(), UriKind.Absolute, out Uri? uri))
                    {
                        Server.SetValue(_Context.Session.Profile.Name);
                        if (!String.IsNullOrWhiteSpace(url)) Error = "Enter an absolute http or https URL.";
                        return;
                    }

                    ServerProfile profile = _Context.Prefs.UpsertProfile(uri.Authority, uri.GetLeftPart(UriPartial.Authority));
                    _Context.Prefs.Save();
                    SwitchTo(profile);
                });
                return;
            }

            ServerProfile? chosen = _Context.Prefs.FindProfile(name);
            if (chosen != null && !ReferenceEquals(chosen, _Context.Session.Profile)) SwitchTo(chosen);
        }

        private void SwitchTo(ServerProfile profile)
        {
            _Context.Session.SwitchProfile(profile);
            Email.Value = profile.LastUser ?? "";
            Step = LoginStepEnum.Email;
            Error = null;
            RefreshPickers();
            Rebuild();
            _ = Task.Run(async () =>
            {
                if (await _Context.Loc.LoadAsync(_Context.Client).ConfigureAwait(false)) _Context.Dispatcher.Post(() => { _Context.Loc.SetLocale(_Context.Loc.Locale); RefreshPickers(); });
                await _Context.Session.TryResumeAsync(null).ConfigureAwait(false);
            });
        }

        private void SetLanguage(string? code)
        {
            string locale = _Context.Loc.SetLocale(code);
            _Context.Prefs.Current.Locale = locale;
            _Context.Prefs.Save();
            _Context.Client.Options.AcceptLanguage = locale;
        }

        private void Label(ISurface surface, int x, int y, string text)
        {
            SurfaceText.Draw(surface, x, y, text, Theme.Muted, 13);
        }

        private static void Center(ISurface surface, ref int y, string text, CellStyle style, int width)
        {
            if (y >= surface.Size.Height) return;
            SurfaceText.Draw(surface, Math.Max(0, (width - TextCells.Width(text)) / 2), y, text, style, width);
            y++;
        }

        #endregion
    }
}
