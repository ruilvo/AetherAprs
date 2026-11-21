// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android.Security;
using Android.Security.Keystore;
using Android.Util;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System;
using System.Text;
using AetherAprs.Services.Platform;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Encrypts local secrets with an AES-GCM key held by the Android Keystore.
/// </summary>
public sealed class AndroidKeystoreSecretProtector : ISecretProtector
{
    private const string Prefix = "ks1:";
    private const string KeyAlias = "aetheraprs.aprs-is-passcode";
    private const string KeyStoreName = "AndroidKeyStore";

    public bool IsProtected(string value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (IsProtected(secret))
        {
            return secret;
        }

        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
        var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(secret))!;
        var iv = cipher.GetIV()!;
        var payload = new byte[iv.Length + encrypted.Length];
        Buffer.BlockCopy(iv, 0, payload, 0, iv.Length);
        Buffer.BlockCopy(encrypted, 0, payload, iv.Length, encrypted.Length);
        return Prefix + Base64.EncodeToString(payload, Base64Flags.NoWrap);
    }

    public string Unprotect(string protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);

        if (!IsProtected(protectedSecret))
        {
            return protectedSecret;
        }

        try
        {
            var payload = Base64.Decode(protectedSecret[Prefix.Length..], Base64Flags.Default)!;
            const int ivLength = 12;
            var iv = new byte[ivLength];
            var encrypted = new byte[payload.Length - ivLength];
            Buffer.BlockCopy(payload, 0, iv, 0, ivLength);
            Buffer.BlockCopy(payload, ivLength, encrypted, 0, encrypted.Length);

            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(
                CipherMode.DecryptMode,
                GetOrCreateKey(),
                new GCMParameterSpec(128, iv));
            return Encoding.UTF8.GetString(cipher.DoFinal(encrypted)!);
        }
        catch (Exception ex) when (ex is Java.Security.GeneralSecurityException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException("Stored APRS-IS credential cannot be decrypted.", ex);
        }
    }

    private static IKey GetOrCreateKey()
    {
        var keyStore = KeyStore.GetInstance(KeyStoreName)!;
        keyStore.Load(null);

        if (keyStore.ContainsAlias(KeyAlias))
        {
            return keyStore.GetKey(KeyAlias, null)!;
        }

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreName)!;
        var spec = new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .Build();
        generator.Init(spec);
        return generator.GenerateKey()!;
    }
}
