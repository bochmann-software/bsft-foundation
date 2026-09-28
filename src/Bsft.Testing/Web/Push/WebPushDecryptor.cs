using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Bsft.Testing.Web.Push;

/// <summary>
/// The browser's half of RFC 8291: reads an <c>aes128gcm</c> body back to its plaintext.
/// </summary>
/// <remarks>
/// It exists so that a test can assert what a person would have seen rather than that some bytes were
/// posted. Decryption is also the strongest possible assertion about the sender: an AEAD tag verifies
/// or it does not, so a body whose key derivation, salt placement or record framing is wrong fails
/// here rather than passing a comparison nobody wrote carefully enough.
/// </remarks>
internal static class WebPushDecryptor
{
    private const int SaltLength = 16;
    private const int KeyLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int CoordinateLength = 32;

    private static readonly byte[] KeyInfoPrefix = Encoding.ASCII.GetBytes("WebPush: info\0");
    private static readonly byte[] ContentEncryptionKeyInfo = Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0");
    private static readonly byte[] NonceInfo = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");

    /// <summary>
    /// Decrypts one body.
    /// </summary>
    /// <param name="body">The complete <c>aes128gcm</c> body.</param>
    /// <param name="recipient">The browser's key pair.</param>
    /// <param name="authSecret">The browser's authentication secret.</param>
    /// <param name="recipientPoint">The browser's public point, which is part of the key derivation.</param>
    /// <returns>The plaintext as text.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the body is not a well-formed single record.</exception>
    /// <exception cref="CryptographicException">Thrown when the record does not authenticate.</exception>
    internal static string Decrypt(
        byte[] body,
        ECDiffieHellman recipient,
        byte[] authSecret,
        byte[] recipientPoint)
    {
        var header = ReadHeader(body);

        using var sender = ECDiffieHellman.Create(
            new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = header.SenderPoint[1..(1 + CoordinateLength)],
                    Y = header.SenderPoint[(1 + CoordinateLength)..],
                },
            });

        var (key, nonce) = DeriveKeys(
            recipient.DeriveRawSecretAgreement(sender.PublicKey),
            authSecret,
            header.Salt,
            recipientPoint,
            header.SenderPoint);

        return Unseal(key, nonce, header.Record);
    }

    /// <summary>
    /// Splits the RFC 8188 header off the record.
    /// </summary>
    /// <param name="body">The complete body.</param>
    /// <returns>The salt, the sender's public point and the sealed record.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the body is too short or the key id is not a point.</exception>
    private static (byte[] Salt, byte[] SenderPoint, byte[] Record) ReadHeader(byte[] body)
    {
        const int fixedHeaderLength = SaltLength + sizeof(uint) + 1;

        if (body.Length <= fixedHeaderLength)
            throw new InvalidOperationException("The push body is shorter than an aes128gcm header.");

        var keyIdLength = body[SaltLength + sizeof(uint)];
        var recordStart = fixedHeaderLength + keyIdLength;

        if (keyIdLength != 1 + CoordinateLength + CoordinateLength || body.Length <= recordStart + TagLength)
            throw new InvalidOperationException(
                "The push body does not carry an uncompressed P-256 point as its key id, so it was not built for Web "
                + "Push.");

        // The record size in the header is read and deliberately not enforced: this fake receives one
        // record, and a sender that declared a larger window than it used is doing nothing wrong.
        _ = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(SaltLength));

        return (body[..SaltLength], body[fixedHeaderLength..recordStart], body[recordStart..]);
    }

    /// <summary>
    /// Derives the content-encryption key and nonce, exactly as the sender did.
    /// </summary>
    /// <param name="sharedSecret">The raw ECDH agreement.</param>
    /// <param name="authSecret">The subscription's authentication secret.</param>
    /// <param name="salt">The record's salt.</param>
    /// <param name="recipientPoint">The browser's public point.</param>
    /// <param name="senderPoint">The sender's ephemeral public point.</param>
    /// <returns>The key and the nonce.</returns>
    private static (byte[] Key, byte[] Nonce) DeriveKeys(
        byte[] sharedSecret,
        byte[] authSecret,
        byte[] salt,
        byte[] recipientPoint,
        byte[] senderPoint)
    {
        var keyInfo = new byte[KeyInfoPrefix.Length + recipientPoint.Length + senderPoint.Length];
        KeyInfoPrefix.CopyTo(keyInfo, 0);
        recipientPoint.CopyTo(keyInfo, KeyInfoPrefix.Length);
        senderPoint.CopyTo(keyInfo, KeyInfoPrefix.Length + recipientPoint.Length);

        var inputKeyMaterial = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sharedSecret,
            CoordinateLength,
            authSecret,
            keyInfo);

        var pseudoRandomKey = HKDF.Extract(HashAlgorithmName.SHA256, inputKeyMaterial, salt);

        return (
            HKDF.Expand(HashAlgorithmName.SHA256, pseudoRandomKey, KeyLength, ContentEncryptionKeyInfo),
            HKDF.Expand(HashAlgorithmName.SHA256, pseudoRandomKey, NonceLength, NonceInfo));
    }

    /// <summary>
    /// Opens the record and strips the padding delimiter.
    /// </summary>
    /// <param name="key">The content-encryption key.</param>
    /// <param name="nonce">The nonce.</param>
    /// <param name="record">The sealed record.</param>
    /// <returns>The plaintext as text.</returns>
    private static string Unseal(byte[] key, byte[] nonce, byte[] record)
    {
        var plaintext = new byte[record.Length - TagLength];

        using var aes = new AesGcm(key, TagLength);

        aes.Decrypt(
            nonce,
            record.AsSpan(0, plaintext.Length),
            record.AsSpan(plaintext.Length),
            plaintext);

        // The last byte is RFC 8188's delimiter, which says whether this was the final record. It is
        // framing rather than content and never belongs in what a test compares against.
        return Encoding.UTF8.GetString(plaintext.AsSpan(0, plaintext.Length - 1));
    }
}
