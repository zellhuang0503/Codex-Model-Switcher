using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexModelSwitcher;

internal static class CredentialVault
{
    private const string TargetPrefix = "CodexModelSwitcher/provider/";
    private const int MaximumSecretCharacters = 1200;
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static bool Exists(string providerId)
    {
        ValidateProviderId(providerId);
        if (!CredRead(BuildTarget(providerId), CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return false;
            }

            throw new Win32Exception(error, "無法讀取 Windows 認證管理員。");
        }

        CredFree(pointer);
        return true;
    }

    public static void Save(string providerId, string secret)
    {
        ValidateProviderId(providerId);
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > MaximumSecretCharacters || secret.Any(char.IsControl))
        {
            throw new ArgumentException("API Key 格式不正確。", nameof(secret));
        }

        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = IntPtr.Zero;
        try
        {
            blob = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = BuildTarget(providerId),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = providerId
            };

            if (!CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "無法保存至 Windows 認證管理員。");
            }
        }
        finally
        {
            Array.Clear(bytes);
            if (blob != IntPtr.Zero)
            {
                for (var offset = 0; offset < bytes.Length; offset++)
                {
                    Marshal.WriteByte(blob, offset, 0);
                }

                Marshal.FreeHGlobal(blob);
            }
        }
    }

    public static string Read(string providerId)
    {
        ValidateProviderId(providerId);
        if (!CredRead(BuildTarget(providerId), CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            throw error == ErrorNotFound
                ? new InvalidOperationException("尚未設定此供應商的 API Key。")
                : new Win32Exception(error, "無法讀取 Windows 認證管理員。");
        }

        byte[]? bytes = null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                throw new InvalidOperationException("保存的 API Key 無法使用。");
            }

            bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            if (bytes is not null)
            {
                Array.Clear(bytes);
            }

            CredFree(pointer);
        }
    }

    public static bool Delete(string providerId)
    {
        ValidateProviderId(providerId);
        if (CredDelete(BuildTarget(providerId), CredentialTypeGeneric, 0))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            return false;
        }

        throw new Win32Exception(error, "無法從 Windows 認證管理員刪除金鑰。");
    }

    private static string BuildTarget(string providerId) => TargetPrefix + providerId;

    private static void ValidateProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId) ||
            providerId.Length > 48 ||
            providerId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            throw new ArgumentException("供應商識別碼格式不正確。", nameof(providerId));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
