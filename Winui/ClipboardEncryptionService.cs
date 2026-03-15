using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using Pelicano.Models;
using Pelicano.Services;

namespace Pelicano;

/// <summary>
/// 사용자별 보호 키로 히스토리 페이로드를 AES-GCM으로 암호화/복호화한다.
/// </summary>
internal sealed class ClipboardEncryptionService
{
    private const int CryptprotectUiForbidden = 0x1;
    private const int TagSizeInBytes = 16;
    private static readonly JsonSerializerOptions SerializerOptions = new();
    private readonly string _protectedKeyPath;
    private readonly Logger _logger;
    private byte[]? _dataKey;

    public ClipboardEncryptionService(string protectedKeyPath, Logger logger)
    {
        _protectedKeyPath = protectedKeyPath;
        _logger = logger;
    }

    public StoredClipboardItem Protect(ClipboardItem item)
    {
        var payload = new ClipboardPayload
        {
            RawText = item.PlainText,
            NormalizedText = item.NormalizedText,
            SourceFormat = item.SourceFormat,
            FileDropPaths = item.FileDropPaths.ToList(),
            ImageBytes = item.ImageBytes.ToArray()
        };

        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var encryptedPayload = Encrypt(plainBytes, nonce);

        return new StoredClipboardItem
        {
            Id = item.Id,
            ItemKind = item.ItemKind,
            ContentHash = item.ContentHash,
            CapturedAt = item.CapturedAt,
            Version = 1,
            Nonce = nonce,
            EncryptedPayload = encryptedPayload
        };
    }

    public bool TryUnprotect(StoredClipboardItem record, out ClipboardPayload? payload)
    {
        try
        {
            var plainBytes = Decrypt(record.EncryptedPayload, record.Nonce);
            payload = JsonSerializer.Deserialize<ClipboardPayload>(plainBytes, SerializerOptions) ??
                      new ClipboardPayload();
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error($"암호화된 히스토리 항목 복호화에 실패했다. Id={record.Id}", exception);
            payload = null;
            return false;
        }
    }

    private byte[] Encrypt(byte[] plainBytes, byte[] nonce)
    {
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aesGcm = new AesGcm(GetOrCreateDataKey(), TagSizeInBytes);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var encryptedPayload = new byte[cipherBytes.Length + tag.Length];
        Buffer.BlockCopy(cipherBytes, 0, encryptedPayload, 0, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, encryptedPayload, cipherBytes.Length, tag.Length);
        return encryptedPayload;
    }

    private byte[] Decrypt(byte[] encryptedPayload, byte[] nonce)
    {
        if (encryptedPayload.Length < 16)
        {
            throw new CryptographicException("암호문 길이가 너무 짧다.");
        }

        var cipherLength = encryptedPayload.Length - 16;
        var cipherBytes = new byte[cipherLength];
        var tag = new byte[16];
        Buffer.BlockCopy(encryptedPayload, 0, cipherBytes, 0, cipherLength);
        Buffer.BlockCopy(encryptedPayload, cipherLength, tag, 0, tag.Length);

        var plainBytes = new byte[cipherBytes.Length];
        using var aesGcm = new AesGcm(GetOrCreateDataKey(), TagSizeInBytes);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return plainBytes;
    }

    private byte[] GetOrCreateDataKey()
    {
        if (_dataKey is not null)
        {
            return _dataKey;
        }

        if (!File.Exists(_protectedKeyPath))
        {
            var newKey = RandomNumberGenerator.GetBytes(32);
            var protectedKey = ProtectForCurrentUser(newKey);
            AtomicFileWriter.WriteAllBytes(_protectedKeyPath, protectedKey);
            _dataKey = newKey;
            return _dataKey;
        }

        var protectedBytes = File.ReadAllBytes(_protectedKeyPath);
        _dataKey = UnprotectForCurrentUser(protectedBytes);
        return _dataKey;
    }

    private static byte[] ProtectForCurrentUser(byte[] plainBytes)
    {
        var inputBlob = CreateDataBlob(plainBytes);

        try
        {
            if (!CryptProtectData(
                    ref inputBlob,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptprotectUiForbidden,
                    out var protectedBlob))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            return ReadAndFreeProtectedBlob(protectedBlob);
        }
        finally
        {
            FreeAllocatedBlob(inputBlob);
        }
    }

    private static byte[] UnprotectForCurrentUser(byte[] protectedBytes)
    {
        var inputBlob = CreateDataBlob(protectedBytes);

        try
        {
            if (!CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptprotectUiForbidden,
                    out var plainBlob))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            return ReadAndFreeProtectedBlob(plainBlob);
        }
        finally
        {
            FreeAllocatedBlob(inputBlob);
        }
    }

    private static DataBlob CreateDataBlob(byte[] data)
    {
        var pointer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, pointer, data.Length);
        return new DataBlob
        {
            cbData = data.Length,
            pbData = pointer
        };
    }

    private static byte[] ReadAndFreeProtectedBlob(DataBlob blob)
    {
        try
        {
            var bytes = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
            return bytes;
        }
        finally
        {
            if (blob.pbData != IntPtr.Zero)
            {
                LocalFree(blob.pbData);
            }
        }
    }

    private static void FreeAllocatedBlob(DataBlob blob)
    {
        if (blob.pbData != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.pbData);
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }
}
