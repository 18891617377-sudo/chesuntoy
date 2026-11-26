using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace MetroUninstaller
{
    /// <summary>基于 WinVerifyTrust 的 PE 签名校验，附带证书主题提取。</summary>
    public class SignatureInfo
    {
        public string State = "unknown";    // valid / unsigned / invalid / missing / unknown
        public string Signer = "";
        public bool MicrosoftSigned;
        public bool CatalogUnavailable;
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CatalogInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string CatalogFile;
        }

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptCATAdminAcquireContext(out IntPtr phCatAdmin, ref Guid pgSubsystem, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptCATAdminAcquireContext2(out IntPtr phCatAdmin, ref Guid pgSubsystem, string pwszHashAlgorithm, IntPtr pStrongHashPolicy, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptCATAdminCalcHashFromFileHandle(IntPtr hFile, ref uint pcbHash, byte[] pbHash, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr hCatAdmin, IntPtr hFile, ref uint pcbHash, byte[] pbHash, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr hCatAdmin, byte[] pbHash, uint cbHash, uint dwFlags, ref IntPtr phPrevCatInfo);

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptCATCatalogInfoFromContext(IntPtr hCatInfo, ref CatalogInfo psCatInfo, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);

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

                // Windows 自带驱动大多是“目录签名”（cat 文件），文件本身没有嵌入签名。
                // 不查目录会把 null.sys / portcls.sys 这类系统驱动误判成无签名。
                if (info.State != "valid")
                {
                    string catalogSigner;
                    int cat = VerifyCatalog(path, out catalogSigner);
                    if (cat == 1)
                    {
                        info.State = "valid";
                        info.Signer = catalogSigner;
                    }
                    else if (cat < 0)
                    {
                        // 没有管理员权限时无法打开驱动 cat 目录，不能把系统文件误判成无签名。
                        info.CatalogUnavailable = true;
                        info.State = "unknown";
                    }
                }
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

            if (string.IsNullOrEmpty(info.Signer)) info.Signer = ReadSigner(path);
            string s = (info.Signer ?? "").ToLowerInvariant();
            info.MicrosoftSigned = s.Contains("microsoft") || s.Contains("windows publisher");
            if (info.State == "valid" && info.Signer.Length == 0) info.MicrosoftSigned = false;
            return info;
        }

        private static int VerifyFileEmbedded(string path)
        {
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

                data.dwStateAction = WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(data, pData, false);
                WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, pData);
                return result;
            }
            catch { return unchecked((int)0x80004005); }
            finally
            {
                if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile);
                if (pData != IntPtr.Zero) Marshal.FreeHGlobal(pData);
            }
        }

        /// <summary>目录签名校验：用文件哈希在 cat 目录中查找，再验证 cat 的签名。
        /// 返回 1=有效目录签名，0=未找到，-1=没有权限打开目录。</summary>
        private static int VerifyCatalog(string path, out string signer)
        {
            signer = "";
            IntPtr admin = IntPtr.Zero;
            FileStream fs = null;
            bool sha256 = false;
            try
            {
                Guid driverSubsystem = new Guid("DE351A42-8E59-11D0-8C47-00C04FC295EE");
                sha256 = CryptCATAdminAcquireContext2(out admin, ref driverSubsystem, "SHA256", IntPtr.Zero, 0);
                if (!sha256 && !CryptCATAdminAcquireContext(out admin, ref driverSubsystem, 0))
                {
                    int err = Marshal.GetLastWin32Error();
                    return (err == 5) ? -1 : 0;
                }
                fs = File.OpenRead(path);
                uint hashLen = 0;
                bool ok = sha256
                    ? CryptCATAdminCalcHashFromFileHandle2(admin, fs.SafeFileHandle.DangerousGetHandle(), ref hashLen, null, 0)
                    : CryptCATAdminCalcHashFromFileHandle(fs.SafeFileHandle.DangerousGetHandle(), ref hashLen, null, 0);
                if (!ok) return (Marshal.GetLastWin32Error() == 5) ? -1 : 0;
                byte[] hash = new byte[hashLen];
                ok = sha256
                    ? CryptCATAdminCalcHashFromFileHandle2(admin, fs.SafeFileHandle.DangerousGetHandle(), ref hashLen, hash, 0)
                    : CryptCATAdminCalcHashFromFileHandle(fs.SafeFileHandle.DangerousGetHandle(), ref hashLen, hash, 0);
                if (!ok) return (Marshal.GetLastWin32Error() == 5) ? -1 : 0;

                IntPtr prev = IntPtr.Zero;
                while (true)
                {
                    IntPtr cat = CryptCATAdminEnumCatalogFromHash(admin, hash, hashLen, 0, ref prev);
                    if (cat == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        return (err == 5) ? -1 : 0;
                    }
                    try
                    {
                        CatalogInfo ci = new CatalogInfo();
                        ci.cbStruct = (uint)Marshal.SizeOf(typeof(CatalogInfo));
                        if (CryptCATCatalogInfoFromContext(cat, ref ci, 0))
                        {
                            string catPath = ci.CatalogFile;
                            if (!string.IsNullOrEmpty(catPath))
                            {
                                if (!Path.IsPathRooted(catPath))
                                {
                                    string catRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                        @"System32\CatRoot\{F750E6C3-38EE-11D1-85E5-00C04FC295EE}");
                                    catPath = Path.Combine(catRoot, catPath);
                                }
                                if (File.Exists(catPath) && VerifyFileEmbedded(catPath) == 0)
                                {
                                    signer = ReadSigner(catPath);
                                    return 1;
                                }
                            }
                        }
                    }
                    finally { CryptCATAdminReleaseCatalogContext(admin, cat, 0); }
                }
            }
            catch { return -1; }
            finally
            {
                if (fs != null) fs.Dispose();
                if (admin != IntPtr.Zero) CryptCATAdminReleaseContext(admin, 0);
            }
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


