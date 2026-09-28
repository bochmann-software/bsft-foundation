using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Bsft.Testing.Web.Push;
using Microsoft.AspNetCore.WebUtilities;

namespace Bsft.Testing.Tests.Support;

/// <summary>
/// The sender's half of RFC 8291: turns text into the <c>aes128gcm</c> body a push carries.
/// </summary>
/// <remarks>
/// It is written out here rather than taken from <c>Bsft.Notifications.WebPush</c> on purpose. The
/// fake push service is meant to work for anything that pushes, so its own suite must not depend on
/// the one sender that happens to live in this repository — and an independent implementation is also
/// what makes a green decryption test mean something, rather than one bug agreeing with itself.
/// </remarks>
internal static class WebPushEncryption
{
    private const int SaltLength = 16;
    private const int KeyLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int CoordinateLength = 32;
    private const uint RecordSize = 4096;
    private const byte LastRecordDelimiter = 0x02;

    private static readonly byte[] KeyInfoPrefix = Encoding.ASCII.GetBytes("WebPush: info\0");
    private static readonly byte[] ContentEncryptionKeyInfo = Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0");
    private static readonly byte[] NonceInfo = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");

    /// <summary>
    /// Encrypts one payload to a subscription.
    /// </summary>
    /// <param name="payload">The text a person would read.</param>
    /// <param name="subscription">The browser's subscription, as the fake handed it out.</param>
    /// <returns>The complete body, header and single record.</returns>
    internal static byte[] Encrypt(string payload, FakePushSubscription subscription)
    {
        var recipientPoint = WebEncoders.Base64UrlDecode(subscription.P256dh);
        var authSecret = WebEncoders.Base64UrlDecode(subscription.Auth);

        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var recipient = ECDiffieHellman.Create(
            new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = recipientPoint[1..(1 + CoordinateLength)],
                    Y = recipientPoint[(1 + CoordinateLength)..],
                },
            });

        var ephemeralPoint = EncodePoint(ephemeral.ExportParameters(includePrivateParameters: false).Q);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);

        var (key, nonce) = DeriveKeys(
            ephemeral.DeriveRawSecretAgreement(recipient.PublicKey),
            authSecret,
            salt,
            recipientPoint,
            ephemeralPoint);

        return Assemble(salt, ephemeralPoint, Seal(key, nonce, Encoding.UTF8.GetBytes(payload)));
    }

    private static byte[] EncodePoint(ECPoint point)
    {
        var encoded = new byte[1 + CoordinateLength + CoordinateLength];
        encoded[0] = 0x04;

        point.X!.CopyTo(encoded.AsSpan(1));
        point.Y!.CopyTo(encoded.AsSpan(1 + CoordinateLength));

        return encoded;
    }

    private static (byte[] Key, byte[] Nonce) DeriveKeys(
        byte[] sharedSecret,
        byte[] authSecret,
        byte[] salt,
        byte[] recipientPoint,
        byte[] ephemeralPoint)
    {
        var keyInfo = new byte[KeyInfoPrefix.Length + recipientPoint.Length + ephemeralPoint.Length];
        KeyInfoPrefix.CopyTo(keyInfo, 0);
        recipientPoint.CopyTo(keyInfo, KeyInfoPrefix.Length);
        ephemeralPoint.CopyTo(keyInfo, KeyInfoPrefix.Length + recipientPoint.Length);

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

    private static byte[] Seal(byte[] key, byte[] nonce, byte[] payload)
    {
        var plaintext = new byte[payload.Length + 1];
        payload.CopyTo(plaintext, 0);
        plaintext[^1] = LastRecordDelimiter;

        var record = new byte[plaintext.Length + TagLength];

        using var aes = new AesGcm(key, TagLength);

        aes.Encrypt(nonce, plaintext, record.AsSpan(0, plaintext.Length), record.AsSpan(plaintext.Length));

        return record;
    }

    private static byte[] Assemble(byte[] salt, byte[] ephemeralPoint, byte[] record)
    {
        var headerLength = SaltLength + sizeof(uint) + 1 + ephemeralPoint.Length;
        var body = new byte[headerLength + record.Length];

        salt.CopyTo(body, 0);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(SaltLength), RecordSize);
        body[SaltLength + sizeof(uint)] = (byte)ephemeralPoint.Length;
        ephemeralPoint.CopyTo(body, SaltLength + sizeof(uint) + 1);
        record.CopyTo(body, headerLength);

        return body;
    }
}
