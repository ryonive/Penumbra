﻿using System.Text.Json.Nodes;

namespace Penumbra.CrashHandler.Buffers;

/// <summary> Only expose the write interface for the buffer. </summary>
public interface IModdedFileBufferWriter
{
    /// <summary> Write a line into the buffer with the given data. </summary>
    /// <param name="characterAddress"> The address of the related character, if known. </param>
    /// <param name="characterName"> The name of the related character, anonymized or relying on index if unavailable, if known. </param>
    /// <param name="collectionName"> The name of the associated collection. Not anonymized. </param>
    /// <param name="requestedFileName"> The file name as requested by the game. </param>
    /// <param name="actualFileName"> The actual modded file name loaded. </param>
    public void WriteLine(nint characterAddress, ReadOnlySpan<byte> characterName, string collectionName, ReadOnlySpan<byte> requestedFileName,
        ReadOnlySpan<byte> actualFileName);
}

/// <summary> The full crash entry for a loaded modded file. </summary>
public record struct ModdedFileLoadedEntry(
    double Age,
    DateTimeOffset Timestamp,
    int ThreadId,
    string CharacterName,
    string CharacterAddress,
    string CollectionName,
    string RequestedFileName,
    string ActualFileName) : ICrashDataEntry;

internal sealed class ModdedFileBuffer : MemoryMappedBuffer, IModdedFileBufferWriter, IBufferReader
{
    private const int _version = 1;
    private const int _lineCount = 128;
    private const int _lineCapacity = 1024;
    private const string _name = "Penumbra.ModdedFile";

    public void WriteLine(nint characterAddress, ReadOnlySpan<byte> characterName, string collectionName, ReadOnlySpan<byte> requestedFileName,
        ReadOnlySpan<byte> actualFileName)
    {
        var accessor = GetCurrentLineLocking();
        lock (accessor)
        {
            accessor.Write(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            accessor.Write(8, Environment.CurrentManagedThreadId);
            accessor.Write(12, characterAddress);
            var span = GetSpan(accessor, 20, 80);
            WriteSpan(characterName, span);
            span = GetSpan(accessor, 92, 80);
            WriteString(collectionName, span);
            span = GetSpan(accessor, 172, 260);
            WriteSpan(requestedFileName, span);
            span = GetSpan(accessor, 432);
            WriteSpan(actualFileName, span);
        }
    }

    public uint TotalCount
        => TotalWrittenLines;

    public IEnumerable<JsonObject> GetLines(DateTimeOffset crashTime)
    {
        var lineCount = (int)CurrentLineCount;
        for (var i = lineCount - 1; i >= 0; --i)
        {
            var line = GetLine(i);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(BitConverter.ToInt64(line));
            var thread = BitConverter.ToInt32(line[8..]);
            var address = BitConverter.ToUInt64(line[12..]);
            var characterName = ReadString(line[20..]);
            var collectionName = ReadString(line[92..]);
            var requestedFileName = ReadString(line[172..]);
            var actualFileName = ReadString(line[432..]);
            yield return new JsonObject()
            {
                [nameof(ModdedFileLoadedEntry.Age)] = (crashTime - timestamp).TotalSeconds,
                [nameof(ModdedFileLoadedEntry.Timestamp)] = timestamp,
                [nameof(ModdedFileLoadedEntry.ThreadId)] = thread,
                [nameof(ModdedFileLoadedEntry.CharacterName)] = characterName,
                [nameof(ModdedFileLoadedEntry.CharacterAddress)] = address.ToString("X"),
                [nameof(ModdedFileLoadedEntry.CollectionName)] = collectionName,
                [nameof(ModdedFileLoadedEntry.RequestedFileName)] = requestedFileName,
                [nameof(ModdedFileLoadedEntry.ActualFileName)] = actualFileName,
            };
        }
    }

    public static IBufferReader CreateReader()
        => new ModdedFileBuffer(false);

    public static IModdedFileBufferWriter CreateWriter()
        => new ModdedFileBuffer();

    private ModdedFileBuffer(bool writer)
        : base(_name, _version)
    { }

    private ModdedFileBuffer()
        : base(_name, _version, _lineCount, _lineCapacity)
    { }
}
