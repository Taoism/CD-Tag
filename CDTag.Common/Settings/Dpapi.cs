using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CDTag.Common.Settings
{
    /// <summary>
    /// Encrypts small secrets (the Discogs token) for the current Windows user with DPAPI, so options.json doesn't hold
    /// them as plain text. Another user, or a copy of the file on another PC, can't read them back.
    /// </summary>
    public static class Dpapi
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CD-Tag options.json");

        /// <summary>Base64 of the encrypted text; null for null or empty text.</summary>
        public static string Protect(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            byte[] result = Run(Encoding.UTF8.GetBytes(text), protect: true);
            return Convert.ToBase64String(result);
        }

        /// <summary>The text back, or null when it is empty or can't be decrypted (another user or PC).</summary>
        public static string Unprotect(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                return null;
            try
            {
                return Encoding.UTF8.GetString(Run(Convert.FromBase64String(base64.Trim()), protect: false));
            }
            catch (Exception ex) when (ex is FormatException || ex is Win32Exception)
            {
                return null;
            }
        }

        private static byte[] Run(byte[] input, bool protect)
        {
            var dataIn = new DATA_BLOB();
            var entropy = new DATA_BLOB();
            var dataOut = new DATA_BLOB();
            GCHandle inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            GCHandle entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
            try
            {
                dataIn.cbData = input.Length;
                dataIn.pbData = inHandle.AddrOfPinnedObject();
                entropy.cbData = Entropy.Length;
                entropy.pbData = entropyHandle.AddrOfPinnedObject();

                const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;
                bool ok = protect
                    ? CryptProtectData(ref dataIn, null, ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref dataOut)
                    : CryptUnprotectData(ref dataIn, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref dataOut);
                if (!ok)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                byte[] result = new byte[dataOut.cbData];
                Marshal.Copy(dataOut.pbData, result, 0, dataOut.cbData);
                return result;
            }
            finally
            {
                inHandle.Free();
                entropyHandle.Free();
                if (dataOut.pbData != IntPtr.Zero)
                    LocalFree(dataOut.pbData);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, ref DATA_BLOB pOptionalEntropy,
                                                    IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
                                                      IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
