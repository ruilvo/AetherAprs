// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Atomically persists session state in the caller-selected private app directory.
/// </summary>
public sealed class FileSessionStateStore(string path) : ISessionStateStore
{
    private readonly string _path = path;

    public async Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync(
            stream,
            SessionStateJsonContext.Default.SessionSnapshot,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    SessionStateJsonContext.Default.SessionSnapshot,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
