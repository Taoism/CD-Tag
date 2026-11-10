using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace CDTag.Launcher
{
    /// <summary>Checks that a downloaded installer has a valid Authenticode signature from Microsoft before it runs as administrator.</summary>
    internal static class Authenticode
    {
        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        /// <summary>True when Windows trusts the file's signature and the signer is Microsoft Corporation.</summary>
        public static bool IsSignedByMicrosoft(string path)
        {
            if (!IsTrusted(path))
                return false;
            try
            {
                X509Certificate signer = X509Certificate.CreateFromSignedFile(path);
                return signer.Subject.IndexOf("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return false;
            }
        }

        private static bool IsTrusted(string path)
        {
            IntPtr fileInfo = IntPtr.Zero, guid = IntPtr.Zero, data = IntPtr.Zero;
            IntPtr pathPtr = Marshal.StringToCoTaskMemUni(path);
            try
            {
                var file = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = pathPtr };
                fileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf(file));
                Marshal.StructureToPtr(file, fileInfo, false);

                var trust = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2,            // WTD_UI_NONE
                    fdwRevocationChecks = 0,   // WTD_REVOKE_NONE (offline-friendly; the chain itself is still checked)
                    dwUnionChoice = 1,         // WTD_CHOICE_FILE
                    pFile = fileInfo,
                    dwStateAction = 0,
                    dwProvFlags = 0x00001000   // WTD_CACHE_ONLY_URL_RETRIEVAL
                };
                data = Marshal.AllocCoTaskMem(Marshal.SizeOf(trust));
                Marshal.StructureToPtr(trust, data, false);

                guid = Marshal.AllocCoTaskMem(16);
                Marshal.Copy(GenericVerifyV2.ToByteArray(), 0, guid, 16);

                return WinVerifyTrust(new IntPtr(-1), guid, data) == 0;
            }
            finally
            {
                if (data != IntPtr.Zero) Marshal.FreeCoTaskMem(data);
                if (fileInfo != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfo);
                if (guid != IntPtr.Zero) Marshal.FreeCoTaskMem(guid);
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, IntPtr pgActionID, IntPtr pWVTData);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
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
    }
}
