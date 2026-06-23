using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json;
using Supabase;
using Lunex.Services;
using System.Net;

namespace Lunex.Views
{
    public partial class AuthWindow : Window
    {
        private readonly Client _supabase;
        private bool _isSignUpMode = false;
        private bool _isPasswordVisible = false;
        private bool _isConfirmPasswordVisible = false;
        private int _failedAttempts = 0;
        private DateTime _lastAttempt = DateTime.MinValue;
        // CSRF protection: random state token generated per OAuth attempt
        private string? _oauthState;
        
        // Strikethrough eye icon path data
        private const string EyeOffPath = "M12 7c2.76 0 5 2.24 5 5 0 .65-.13 1.26-.36 1.83l2.92 2.92c1.51-1.26 2.7-2.89 3.43-4.75-1.73-4.39-6-7.5-11-7.5-1.4 0-2.74.25-3.98.7l2.16 2.16C10.74 7.13 11.35 7 12 7zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3 .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-.78l3.15 3.15.02-.16c0-1.66-1.34-3-3-3l-.17.01z";
        // Normal eye icon path data
        private const string EyeOnPath = "M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z";

        public AuthWindow()
        {
            InitializeComponent();
            
            _supabase = SupabaseService.Client;
        }

        private void SetBusyState(bool isBusy)
        {
            LoginButton.IsEnabled = !isBusy;
            GoogleButton.IsEnabled = !isBusy;
            ToggleModeButton.IsEnabled = !isBusy;
            ForgotButton.IsEnabled = !isBusy;
        }

        private void ToggleMode_Click(object sender, RoutedEventArgs e)
        {
            _isSignUpMode = !_isSignUpMode;
            StatusMessage.Visibility = Visibility.Collapsed;

            // Clear state data
            EmailInput.Text = string.Empty;
            PasswordInput.Password = string.Empty;
            PasswordVisibleInput.Text = string.Empty;
            UsernameInput.Text = string.Empty;
            ConfirmPasswordInput.Password = string.Empty;
            ConfirmPasswordVisibleInput.Text = string.Empty;

            if (_isSignUpMode)
            {
                UsernameSection.Visibility = Visibility.Visible;
                ConfirmPasswordSection.Visibility = Visibility.Visible;
                ForgotButton.Visibility = Visibility.Collapsed;
                OrDivider.Visibility = Visibility.Collapsed;
                GoogleButton.Visibility = Visibility.Collapsed;
                LoginButton.Content = "Create Account ➔";
                ToggleModeButton.Content = "Already have an account? Log In";
            }
            else
            {
                UsernameSection.Visibility = Visibility.Collapsed;
                ConfirmPasswordSection.Visibility = Visibility.Collapsed;
                ForgotButton.Visibility = Visibility.Visible;
                OrDivider.Visibility = Visibility.Visible;
                GoogleButton.Visibility = Visibility.Visible;
                LoginButton.Content = "Log In ➔";
                ToggleModeButton.Content = "Need an account? Sign Up";
            }
        }

        private async void MainAction_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailInput.Text?.Trim();
            var password = _isPasswordVisible ? PasswordVisibleInput.Text : PasswordInput.Password;
            var username = UsernameInput.Text?.Trim();
            var confirmPassword = _isConfirmPasswordVisible ? ConfirmPasswordVisibleInput.Text : ConfirmPasswordInput.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError("Please enter both email and password.");
                return;
            }

            try
            {
                // Rate limiting — exponential backoff after failed attempts
                if (_failedAttempts > 0)
                {
                    var cooldownSeconds = Math.Min(30, Math.Pow(2, _failedAttempts));
                    var elapsed = (DateTime.Now - _lastAttempt).TotalSeconds;
                    if (elapsed < cooldownSeconds)
                    {
                        ShowError($"Too many attempts. Please wait {(int)(cooldownSeconds - elapsed)} seconds.");
                        return;
                    }
                }
                _lastAttempt = DateTime.Now;

                SetBusyState(true);
                LoginButton.Content = "Authenticating...";
                StatusMessage.Visibility = Visibility.Collapsed;

                if (_isSignUpMode)
                {
                    if (string.IsNullOrEmpty(username))
                    {
                        ShowError("Please enter a username.");
                        return;
                    }
                    if (password.Length < 8)
                    {
                        ShowError("Password must be at least 8 characters.");
                        return;
                    }
                    if (password != confirmPassword)
                    {
                        ShowError("Passwords do not match.");
                        return;
                    }

                    // Proceed with signup
                    var signUpSession = await _supabase.Auth.SignUp(email, password);
                    if (signUpSession != null)
                    {
                        _failedAttempts = 0; // Reset on success
                        SettingsService.Instance.CloudAuthToken = signUpSession.AccessToken ?? "";
                        SettingsService.Instance.CloudRefreshToken = signUpSession.RefreshToken ?? "";
                        // Save username mapping via profile service or DB later
                        this.DialogResult = true;
                        this.Close();
                    }
                    else
                    {
                        ShowError("Sign up failed or requires email confirmation.");
                    }
                }
                else
                {
                    // Proceed with login
                    var session = await _supabase.Auth.SignIn(email, password);
                    if (session != null && session.AccessToken != null)
                    {
                        _failedAttempts = 0; // Reset on success
                        SettingsService.Instance.CloudAuthToken = session.AccessToken;
                        SettingsService.Instance.CloudRefreshToken = session.RefreshToken ?? "";
                        this.DialogResult = true;
                        this.Close();
                    }
                    else
                    {
                        ShowError("Invalid credentials.");
                    }
                }
            }
            catch (Exception ex)
            {
                _failedAttempts++;
                string parsedMessage = ParseSupabaseError(ex.Message, _isSignUpMode);
                ShowError(parsedMessage);
            }
            finally
            {
                SetBusyState(false);
                LoginButton.Content = _isSignUpMode ? "Create Account ➔" : "Log In ➔";
            }
        }

        private void Input_Changed(object sender, TextChangedEventArgs e)
        {
            if (sender == EmailInput)
            {
                EmailPlaceholder.Visibility = string.IsNullOrEmpty(EmailInput.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (sender == UsernameInput)
            {
                UsernamePlaceholder.Visibility = string.IsNullOrEmpty(UsernameInput.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (sender == PasswordVisibleInput)
            {
                PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordVisibleInput.Text) ? Visibility.Visible : Visibility.Collapsed;
                if (_isPasswordVisible && PasswordInput.Password != PasswordVisibleInput.Text)
                {
                    PasswordInput.Password = PasswordVisibleInput.Text;
                }
            }
            else if (sender == ConfirmPasswordVisibleInput)
            {
                ConfirmPasswordPlaceholder.Visibility = string.IsNullOrEmpty(ConfirmPasswordVisibleInput.Text) ? Visibility.Visible : Visibility.Collapsed;
                if (_isConfirmPasswordVisible && ConfirmPasswordInput.Password != ConfirmPasswordVisibleInput.Text)
                {
                    ConfirmPasswordInput.Password = ConfirmPasswordVisibleInput.Text;
                }
            }
        }

        private void PasswordInput_Changed(object sender, RoutedEventArgs e)
        {
            if (sender == PasswordInput)
            {
                PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordInput.Password) ? Visibility.Visible : Visibility.Collapsed;
                if (!_isPasswordVisible && PasswordVisibleInput.Text != PasswordInput.Password)
                {
                    PasswordVisibleInput.Text = PasswordInput.Password;
                }
            }
            else if (sender == ConfirmPasswordInput)
            {
                ConfirmPasswordPlaceholder.Visibility = string.IsNullOrEmpty(ConfirmPasswordInput.Password) ? Visibility.Visible : Visibility.Collapsed;
                if (!_isConfirmPasswordVisible && ConfirmPasswordVisibleInput.Text != ConfirmPasswordInput.Password)
                {
                    ConfirmPasswordVisibleInput.Text = ConfirmPasswordInput.Password;
                }
            }
        }

        private void TogglePassword_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;
            if (_isPasswordVisible)
            {
                PasswordVisibleInput.Visibility = Visibility.Visible;
                PasswordInput.Visibility = Visibility.Collapsed;
                PasswordEyeIcon.Data = Geometry.Parse(EyeOffPath);
                PasswordVisibleInput.Focus();
                PasswordVisibleInput.CaretIndex = PasswordVisibleInput.Text.Length;
            }
            else
            {
                PasswordVisibleInput.Visibility = Visibility.Collapsed;
                PasswordInput.Visibility = Visibility.Visible;
                PasswordEyeIcon.Data = Geometry.Parse(EyeOnPath);
                PasswordInput.Focus();
                // PasswordBox doesn't have CaretIndex in standard WPF but Focus() is fine
            }
        }

        private void ToggleConfirmPassword_Click(object sender, RoutedEventArgs e)
        {
            _isConfirmPasswordVisible = !_isConfirmPasswordVisible;
            if (_isConfirmPasswordVisible)
            {
                ConfirmPasswordVisibleInput.Visibility = Visibility.Visible;
                ConfirmPasswordInput.Visibility = Visibility.Collapsed;
                ConfirmPasswordEyeIcon.Data = Geometry.Parse(EyeOffPath);
                ConfirmPasswordVisibleInput.Focus();
                ConfirmPasswordVisibleInput.CaretIndex = ConfirmPasswordVisibleInput.Text.Length;
            }
            else
            {
                ConfirmPasswordVisibleInput.Visibility = Visibility.Collapsed;
                ConfirmPasswordInput.Visibility = Visibility.Visible;
                ConfirmPasswordEyeIcon.Data = Geometry.Parse(EyeOnPath);
                ConfirmPasswordInput.Focus();
            }
        }

        private async void Google_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusyState(true);
                ShowError("Opening browser for Google Auth...");

                await HandleGoogleAuthAsync();
            }
            catch (Exception ex)
            {
                ShowError("Google Auth Error: " + ParseSupabaseError(ex.Message, false));
            }
            finally
            {
                SetBusyState(false);
                if (StatusMessage.Text.StartsWith("Opening browser"))
                {
                    StatusMessage.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async Task HandleGoogleAuthAsync()
        {
            var redirectUrl = "http://127.0.0.1:54321/";

            // Generate a cryptographically random state token for CSRF protection
            _oauthState = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUrl);
            listener.Start();

            var state = await _supabase.Auth.SignIn(Supabase.Gotrue.Constants.Provider.Google, new Supabase.Gotrue.SignInOptions { RedirectTo = redirectUrl });
            Process.Start(new ProcessStartInfo(state.Uri.ToString()) { UseShellExecute = true });

            var context = await listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;
            if (request.Url != null && !request.Url.Query.Contains("access_token"))
            {
                string html = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Authenticating | Lunex</title>
    <style>
        body { margin: 0; padding: 0; background-color: #161616; color: #ffffff; font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; overflow: hidden; }
        .container { background: rgba(255, 255, 255, 0.02); border: 1px solid #333333; border-radius: 8px; padding: 40px; text-align: center; box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.37); max-width: 400px; width: 90%; animation: fadeIn 0.5s ease-out; }
        @keyframes fadeIn { from { opacity: 0; transform: translateY(20px); } to { opacity: 1; transform: translateY(0); } }
        h2 { margin-top: 0; font-weight: 600; color: #FF4500; }
        p { color: #9CA3AF; font-size: 15px; line-height: 1.5; margin-bottom: 0; }
        .spinner { width: 40px; height: 40px; border: 3px solid rgba(255, 69, 0, 0.2); border-radius: 50%; border-top-color: #FF4500; animation: spin 1s ease-in-out infinite; margin: 0 auto 20px auto; }
        @keyframes spin { to { transform: rotate(360deg); } }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""spinner""></div>
        <h2>Authenticating...</h2>
        <p>Please wait while we securely log you in to Lunex.</p>
    </div>
    <script>
        if (window.location.hash) {
            window.location.href = '/callback?' + window.location.hash.substring(1);
        } else {
            document.querySelector('h2').innerText = 'Error';
            document.querySelector('p').innerText = 'Authentication failed or no token provided. You can safely close this window.';
            document.querySelector('.spinner').style.display = 'none';
        }
    </script>
</body>
</html>";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(html);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();

                // Wait for the second request that contains the query parameters
                context = await listener.GetContextAsync();
                request = context.Request;
                response = context.Response;
            }

            // Now process the query parameters to complete authentication
            string finalHtml = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Success | Lunex</title>
    <style>
        body { margin: 0; padding: 0; background-color: #161616; color: #ffffff; font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; overflow: hidden; }
        .container { background: rgba(255, 255, 255, 0.02); border: 1px solid #333333; border-radius: 8px; padding: 40px; text-align: center; box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.37); max-width: 400px; width: 90%; animation: popIn 0.5s cubic-bezier(0.175, 0.885, 0.32, 1.275); }
        @keyframes popIn { from { opacity: 0; transform: scale(0.9); } to { opacity: 1; transform: scale(1); } }
        .icon { width: 64px; height: 64px; background: #FF4500; border-radius: 50%; display: flex; justify-content: center; align-items: center; margin: 0 auto 20px auto; box-shadow: 0 0 20px rgba(255, 69, 0, 0.4); }
        .icon svg { width: 32px; height: 32px; fill: none; stroke: #fff; stroke-width: 3; stroke-linecap: round; stroke-linejoin: round; }
        h2 { margin-top: 0; font-weight: 600; font-size: 24px; margin-bottom: 10px; color: #ffffff; }
        p { color: #9CA3AF; font-size: 15px; line-height: 1.5; margin-bottom: 0; }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""icon"">
            <svg viewBox=""0 0 24 24""><polyline points=""20 6 9 17 4 12""></polyline></svg>
        </div>
        <h2>Authentication Successful</h2>
        <p>You can safely close this window and return to Lunex.</p>
    </div>
    <script>setTimeout(() => window.close(), 3000);</script>
</body>
</html>";
            
            // Reconstruct a pseudo-URL with the query parameters acting as a hash fragment so Supabase can parse it
            string fakeUrl = $"http://localhost/{request.Url?.Query?.Replace('?', '#')}";
            
            try
            {
                var session = await _supabase.Auth.GetSessionFromUrl(new Uri(fakeUrl), true);
                if (session != null && session.AccessToken != null)
                {
                    SettingsService.Instance.CloudAuthToken = session.AccessToken;
                    SettingsService.Instance.CloudRefreshToken = session.RefreshToken ?? "";
                    
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(finalHtml);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();

                    // UI changes must be dispatched
                    Dispatcher.Invoke(() => {
                        this.DialogResult = true;
                        this.Close();
                    });
                }
                else
                {
                    throw new Exception("Session could not be established from URL.");
                }
            }
            catch (Exception ex)
            {
                string errorHtml = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Error | Lunex</title>
    <style>
        body {{ margin: 0; padding: 0; background-color: #161616; color: #ffffff; font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; overflow: hidden; }}
        .container {{ background: rgba(255, 255, 255, 0.02); border: 1px solid #333333; border-radius: 8px; padding: 40px; text-align: center; box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.37); max-width: 400px; width: 90%; animation: shake 0.5s; }}
        @keyframes shake {{ 0%, 100% {{ transform: translateX(0); }} 25% {{ transform: translateX(-5px); }} 75% {{ transform: translateX(5px); }} }}
        .icon {{ width: 64px; height: 64px; background: #e63946; border-radius: 50%; display: flex; justify-content: center; align-items: center; margin: 0 auto 20px auto; box-shadow: 0 0 20px rgba(230, 57, 70, 0.4); }}
        .icon svg {{ width: 32px; height: 32px; fill: none; stroke: #fff; stroke-width: 3; stroke-linecap: round; stroke-linejoin: round; }}
        h2 {{ margin-top: 0; font-weight: 600; font-size: 24px; margin-bottom: 10px; color: #ffffff; }}
        p {{ color: #9CA3AF; font-size: 15px; line-height: 1.5; margin-bottom: 0; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""icon"">
            <svg viewBox=""0 0 24 24""><line x1=""18"" y1=""6"" x2=""6"" y2=""18""></line><line x1=""6"" y1=""6"" x2=""18"" y2=""18""></line></svg>
        </div>
        <h2>Authentication Failed</h2>
        <p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p>
    </div>
</body>
</html>";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(errorHtml);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
                throw;
            }
            finally
            {
                listener.Stop();
            }
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            var confirmDialog = new Views.ModernDialog("Skip Login", "Would you like to skip this login screen every time the app starts?\n\nYou can always log in later from the Profile page.", true);
            confirmDialog.Owner = this;
            confirmDialog.ShowDialog();

            if (confirmDialog.Result)
            {
                SettingsService.Instance.SkipLoginOnStartup = true;
            }

            this.DialogResult = false;
            this.Close();
        }

        private void ShowError(string message)
        {
            StatusMessage.Visibility = Visibility.Collapsed;
            var dialog = new Views.ModernDialog("Authentication Error", message);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private string ParseSupabaseError(string rawMessage, bool isSignUp)
        {
            try
            {
                // Try to parse the raw message as JSON
                using var doc = JsonDocument.Parse(rawMessage);
                var root = doc.RootElement;
                
                string parsedMsg = rawMessage;
                if (root.TryGetProperty("msg", out var msgProp))
                {
                    parsedMsg = msgProp.GetString() ?? rawMessage;
                }
                else if (root.TryGetProperty("message", out var messageProp))
                {
                    parsedMsg = messageProp.GetString() ?? rawMessage;
                }

                // Check for specific error codes to provide better guidance
                if (isSignUp && root.TryGetProperty("error_code", out var errCodeProp))
                {
                    if (errCodeProp.GetString() == "user_already_exists")
                    {
                        return "This email is already registered. If you created your account via Google, please use 'Continue with Google' to log in instead.";
                    }
                }

                return parsedMsg;
            }
            catch
            {
                // If it's not valid JSON, just return the original exception message
                return rawMessage;
            }
        }

        public string? SessionJson { get; private set; }
    }
}
