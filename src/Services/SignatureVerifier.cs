using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace MetroUninstaller
{
    /// <summary>基于 WinVerifyTrust 的 PE 签名校验，附带证书主题提取。</summary>
    public class SignatureInfo
    {
        public string State = "unknown";    // valid / unsigned / invalid / missing
        public string Signer = "";
        public bool MicrosoftSigned;
        public int HResult;
    }

    public static class SignatureVerifier
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
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

        private static readonly Guid WinTrustActionGenericVerifyV2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", PreserveSig = true, SetLastError = false, ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr data);

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 0;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x10;

        public static SignatureInfo Check(string path)
        {
            SignatureInfo info = new SignatureInfo();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                info.State = "missing";
                return info;
            }

            IntPtr pFile = IntPtr.Zero;
            IntPtr pData = IntPtr.Zero;
            try
            {
                WinTrustFileInfo fileInfo = new WinTrustFileInfo();
                fileInfo.cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
                fileInfo.FilePath = path;
                pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(fileInfo, pFile, false);

                WinTrustData data = new WinTrustData();
                data.cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustData));
                data.dwUIChoice = WTD_UI_NONE;
                data.fdwRevocationChecks = WTD_REVOKE_NONE;
                data.dwUnionChoice = WTD_CHOICE_FILE;
                data.pFile = pFile;
                data.dwStateAction = WTD_STATEACTION_VERIFY;
                data.dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL;

                pData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
                Marshal.StructureToPtr(data, pData, false);

                int result = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, pData);
                info.HResult = result;

                data.dwStateAction = WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(data, pData, false);
                WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, pData);

                if (result == 0) info.State = "valid";
                else if (unchecked((uint)result) == 0x800B0100u) info.State = "unsigned";
                else if (unchecked((uint)result) == 0x800B010Au) info.State = "unsigned"; // TRUST_E_SUBJECT_FORM_UNKNOWN
                else if (unchecked((uint)result) == 0x800B0107u) info.State = "unsigned";
                else info.State = "invalid";
            }
            catch (Exception ex)
            {
                info.State = "invalid";
                Log.Warn("签名校验失败：" + path + " — " + ex.Message);
            }
            finally
            {
                if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile);
                if (pData != IntPtr.Zero) Marshal.FreeHGlobal(pData);
            }

            info.Signer = ReadSigner(path);
            string s = (info.Signer ?? "").ToLowerInvariant();
            info.MicrosoftSigned = s.Contains("microsoft") || s.Contains("windows publisher");
            if (info.State == "valid" && info.Signer.Length == 0) info.MicrosoftSigned = false;
            return info;
        }

        private static string ReadSigner(string path)
        {
            try
            {
                X509Certificate cert = X509Certificate.CreateFromSignedFile(path);
                if (cert == null) return "";
                X509Certificate2 cert2 = new X509Certificate2(cert);
                string subject = cert2.Subject ?? "";
                string cn = ExtractCn(subject);
                if (cn.Length > 0) return cn;
                return cert2.GetNameInfo(X509NameType.SimpleName, false);
            }
            catch { return ""; }
        }

        private static string ExtractCn(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return "";
            string[] parts = subject.Split(',');
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    return p.Substring(3).Trim('"');
            }
            return "";
        }
    }
}


