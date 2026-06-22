using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace DiscordScheduler
{
    public sealed class DpapiSecretStore : ISecretStore
    {
        private readonly string _folder;
        private readonly IFileSystem _fileSystem;

        public DpapiSecretStore(string folder, IFileSystem fileSystem = null)
        {
            _folder = folder ?? "";
            _fileSystem = fileSystem ?? new SystemFileSystem();
        }

        public SecretStoreStatus Status
        {
            get
            {
                if (!TryResolveDpapi(out _, out _, out _))
                    return SecretStoreStatus.Unavailable;

                if (string.IsNullOrWhiteSpace(_folder))
                    return SecretStoreStatus.Unavailable;

                var ensure = _fileSystem.EnsureDirectory(_folder);
                return ensure.ok ? SecretStoreStatus.Available : SecretStoreStatus.PermissionDenied;
            }
        }

        public SecretStoreResult Save(string key, string secret)
        {
            if (Status != SecretStoreStatus.Available)
                return SecretStoreResult.Fail(Status, "Protected secret store is unavailable.");

            if (string.IsNullOrWhiteSpace(key))
                return SecretStoreResult.Fail(SecretStoreStatus.Unavailable, "Secret key is required.");

            try
            {
                var protectedText = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(secret ?? "")));
                var write = _fileSystem.WriteAllText(PathForKey(key), protectedText);
                return write.ok
                    ? SecretStoreResult.Ok(key)
                    : SecretStoreResult.Fail(SecretStoreStatus.PermissionDenied, write.error);
            }
            catch (Exception exception)
            {
                return SecretStoreResult.Fail(SecretStoreStatus.Corrupt, exception.Message);
            }
        }

        public SecretStoreReadResult Read(string key)
        {
            if (Status != SecretStoreStatus.Available)
                return SecretStoreReadResult.Fail(Status, "Protected secret store is unavailable.");

            if (string.IsNullOrWhiteSpace(key))
                return SecretStoreReadResult.Fail(SecretStoreStatus.MissingCredential, "Secret key is required.");

            var path = PathForKey(key);
            if (!_fileSystem.FileExists(path))
                return SecretStoreReadResult.Fail(SecretStoreStatus.MissingCredential, "Credential is missing.");

            if (!_fileSystem.TryReadAllText(path, out var protectedText, out var error))
                return SecretStoreReadResult.Fail(SecretStoreStatus.PermissionDenied, error);

            try
            {
                var bytes = Convert.FromBase64String(protectedText ?? "");
                var secret = Encoding.UTF8.GetString(Unprotect(bytes));
                return SecretStoreReadResult.Ok(secret);
            }
            catch (Exception exception)
            {
                return SecretStoreReadResult.Fail(SecretStoreStatus.Corrupt, exception.Message);
            }
        }

        public ValidationResult Delete(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return ValidationResult.Ok();

            var result = _fileSystem.DeleteFile(PathForKey(key));
            return result.ok ? ValidationResult.Ok() : ValidationResult.Fail(SecretRedactor.RedactAndTruncate(result.error));
        }

        private string PathForKey(string key)
        {
            return Path.Combine(_folder, HashKey(key) + ".secret");
        }

        private static string HashKey(string key)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? ""));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static byte[] Protect(byte[] bytes)
        {
            if (!TryResolveDpapi(out var protectedData, out var scopeType, out var currentUser))
                throw new InvalidOperationException("DPAPI is not available.");

            return (byte[])protectedData.InvokeMember(
                "Protect",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static,
                null,
                null,
                new object[] { bytes ?? Array.Empty<byte>(), null, currentUser });
        }

        private static byte[] Unprotect(byte[] bytes)
        {
            if (!TryResolveDpapi(out var protectedData, out var scopeType, out var currentUser))
                throw new InvalidOperationException("DPAPI is not available.");

            return (byte[])protectedData.InvokeMember(
                "Unprotect",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static,
                null,
                null,
                new object[] { bytes ?? Array.Empty<byte>(), null, currentUser });
        }

        private static bool TryResolveDpapi(out Type protectedData, out Type scopeType, out object currentUser)
        {
            protectedData =
                Type.GetType("System.Security.Cryptography.ProtectedData, System.Security", false) ??
                Type.GetType("System.Security.Cryptography.ProtectedData", false);
            scopeType =
                Type.GetType("System.Security.Cryptography.DataProtectionScope, System.Security", false) ??
                Type.GetType("System.Security.Cryptography.DataProtectionScope", false);
            currentUser = null;

            if (protectedData == null || scopeType == null)
                return false;

            currentUser = Enum.Parse(scopeType, "CurrentUser");
            return currentUser != null;
        }
    }
}
