using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MetroUninstaller
{
    /// <summary>
    /// 与可选的反 Rootkit 内核驱动 chesunark.sys 通信。
    /// 驱动只调用 ZwUnloadDriver / ZwDeleteKey / ZwDeleteFile，不摘内核对象、不改回调。
    /// </summary>
    public static class KernelDriverClient
    {
        private const string DevicePath = @"\\.\ChesunArk";
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private const uint FileDeviceUnknown = 0x00000022;
        private const uint MethodBuffered = 0;
        private const uint FileAnyAccess = 0;

        private static uint CtlCode(uint function)
        {
            return (FileDeviceUnknown << 16) | (FileAnyAccess << 14) | (function << 2) | MethodBuffered;
        }

        private static readonly uint IoctlUnload = CtlCode(0x800);
        private static readonly uint IoctlDeleteService = CtlCode(0x801);
        private static readonly uint IoctlDeleteFile = CtlCode(0x802);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr file, uint code, byte[] input, uint inputLength,
            byte[] output, uint outputLength, out uint returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public static bool IsAvailable
        {
            get
            {
                IntPtr h = Open();
                if (!IsValid(h)) return false;
                CloseHandle(h);
                return true;
            }
        }

        private static bool IsValid(IntPtr h)
        {
            return h != IntPtr.Zero && h != new IntPtr(-1);
        }

        private static IntPtr Open()
        {
            try
            {
                return CreateFile(DevicePath, GenericRead | GenericWrite, 0, IntPtr.Zero,
                    OpenExisting, 0, IntPtr.Zero);
            }
            catch { return new IntPtr(-1); }
        }

        private static bool Send(uint code, string text, out string error)
        {
            error = "";
            IntPtr h = Open();
            if (!IsValid(h))
            {
                error = "反 Rootkit 驱动未安装或设备未就绪";
                return false;
            }
            try
            {
                byte[] input = Encoding.Unicode.GetBytes((text ?? "") + "\0");
                uint returned;
                bool ok = DeviceIoControl(h, code, input, (uint)input.Length, null, 0, out returned, IntPtr.Zero);
                if (!ok) error = "DeviceIoControl 失败，Win32 错误 " + Marshal.GetLastWin32Error();
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally { CloseHandle(h); }
        }

        /// <summary>请求驱动对指定服务调用 ZwUnloadDriver。驱动没有 Unload 例程时会失败。</summary>
        public static bool TryUnload(string serviceName, out string error)
        {
            if (string.IsNullOrEmpty(serviceName)) { error = "服务名为空"; return false; }
            return Send(IoctlUnload, serviceName, out error);
        }

        /// <summary>请求驱动删除 Services 注册表键（用于热卸载后的残留）。</summary>
        public static bool TryDeleteServiceKey(string serviceName, out string error)
        {
            if (string.IsNullOrEmpty(serviceName)) { error = "服务名为空"; return false; }
            return Send(IoctlDeleteService, serviceName, out error);
        }

        /// <summary>请求驱动删除 NT 路径文件（例如 \??\C:\Windows\System32\drivers\x.sys）。</summary>
        public static bool TryDeleteFile(string ntPath, out string error)
        {
            if (string.IsNullOrEmpty(ntPath)) { error = "路径为空"; return false; }
            return Send(IoctlDeleteFile, ntPath, out error);
        }
    }
}
