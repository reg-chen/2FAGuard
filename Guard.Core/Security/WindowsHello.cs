﻿using System.Runtime.InteropServices;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;

namespace Guard.Core.Security
{
    public partial class WindowsHello
    {
        private static string GetAccountName()
        {
            return $"2FAGuard{(InstallationContext.IsPortable() ? "Portable" : "")}{Auth.GetInstallationID()}";
        }

        public static async Task<KeyCredentialRetrievalResult> Register()
        {
            FocusSecurityPrompt();
            return await KeyCredentialManager.RequestCreateAsync(
                GetAccountName(),
                KeyCredentialCreationOption.FailIfExists
            );
        }

        public static async Task<bool> IsAvailable()
        {
            return await KeyCredentialManager.IsSupportedAsync();
        }

        /*public static async Task<bool> RequestSimpleVerification()
        {
            FocusSecurityPrompt();
            UserConsentVerificationResult consentResult =
                await UserConsentVerifier.RequestVerificationAsync(
                    I18n.GetString("win.hello.request")
                );
            return consentResult == UserConsentVerificationResult.Verified;
        }*/

        /// <summary>
        /// Get a secret key by signing a payload with Windows Hello.
        /// Strategy is based on Bitwarden's Windows Hello implementation:
        /// https://github.com/bitwarden/clients/blob/1f8e6ea6f8a736a8a766e5e0643c7106adfa8433/apps/desktop/desktop_native/src/biometric/windows.rs#L69
        /// </summary>
        /// <returns>A string used as key with argon2id</returns>
        public static async Task<string> GetSignedChallenge()
        {
            FocusSecurityPrompt();
            var openKeyResult = await KeyCredentialManager.OpenAsync(GetAccountName());
            if (openKeyResult.Status != KeyCredentialStatus.Success)
            {
                throw new Exception(
                    $"Failed to authenticate with Windows Hello: {openKeyResult.Status}"
                );
            }
            return await SignChallenge(openKeyResult.Credential, Auth.GetWindowsHelloChallenge());
        }

        public static async Task<string> SignChallenge(KeyCredential credential, string challenge)
        {
            ArgumentNullException.ThrowIfNull(credential);
            var buffer = CryptographicBuffer.ConvertStringToBinary(
                challenge,
                BinaryStringEncoding.Utf8
            );
            var result = await credential.RequestSignAsync(buffer);
            if (result.Status != KeyCredentialStatus.Success)
            {
                throw new Exception($"Failed to sign Windows Hello challenge: {result.Status}");
            }
            return CryptographicBuffer.EncodeToBase64String(result.Result);
        }

        public static async Task Unregister()
        {
            await KeyCredentialManager.DeleteAsync(GetAccountName());
        }

        public static async void FocusSecurityPrompt()
        {
            const string className = "Credential Dialog Xaml Host";
            const int maxTries = 3;

            try
            {
                for (int currentTry = 0; currentTry < maxTries; currentTry++)
                {
                    IntPtr hwnd = FindWindow(className, IntPtr.Zero);
                    if (hwnd != IntPtr.Zero)
                    {
                        SetForegroundWindow(hwnd);
                        return; // Exit the loop if successfully found and focused the window
                    }
                    await Task.Delay(500); // Retry after a delay if the window is not found
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Failed to focus Windows Hello prompt {msg}", ex.Message);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, IntPtr ZeroOnly);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
