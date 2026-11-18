using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace MauSysInfo.Services;

/// <summary>Checks a file's embedded Authenticode signature with Windows' own trust verification.</summary>
public static class Authenticode
{
    /// <summary>
    /// The signer's certificate subject (e.g. "CN=Mausy, O=…") when the file carries a valid, trusted
    /// embedded signature; null when it is unsigned, tampered with, or signed by an untrusted certificate.
    /// </summary>
    public static string? SignerOf(string path)
    {
        if (!File.Exists(path) || !IsTrusted(path)) return null;
        try
        {
#pragma warning disable SYSLIB0057 // reading the signer of a signed file, not loading a certificate
            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsTrusted(string path)
    {
        var fileInfo = new WinTrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            pcwszFilePath = path,
        };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,              // WTD_UI_NONE
                fdwRevocationChecks = 0,     // WTD_REVOKE_NONE
                dwUnionChoice = 1,           // WTD_CHOICE_FILE
                pFile = filePtr,
                dwStateAction = 0,           // WTD_STATEACTION_IGNORE
                dwProvFlags = 0x1000,        // WTD_CACHE_ONLY_URL_RETRIEVAL: no network
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WinTrustData data);
}
