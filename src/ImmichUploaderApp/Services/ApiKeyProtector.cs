using System.Runtime.InteropServices;
using System.Text;

namespace ImmichUploaderApp.Services;

/// <summary>Encrypts secrets for the Windows user that created them (DPAPI).</summary>
internal static class ApiKeyProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    public static string Protect(string value) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputBlob = new DataBlob(input);
        try
        {
            DataBlob outputBlob;
            var ok = protect
                ? CryptProtectData(ref inputBlob, "Immich Uploader API key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);
            if (!ok) throw new InvalidOperationException($"Windows could not protect the API key (error {Marshal.GetLastWin32Error()}).");
            try
            {
                var output = new byte[outputBlob.cbData];
                Marshal.Copy(outputBlob.pbData, output, 0, output.Length);
                return output;
            }
            finally { LocalFree(outputBlob.pbData); }
        }
        finally { inputBlob.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
        public DataBlob(byte[] data)
        {
            cbData = data.Length;
            pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, pbData, data.Length);
        }
        public readonly void Dispose() { if (pbData != IntPtr.Zero) Marshal.FreeHGlobal(pbData); }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr optionalEntropy, IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
