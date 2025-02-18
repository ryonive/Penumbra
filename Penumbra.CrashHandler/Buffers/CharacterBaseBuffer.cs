﻿using System.Text.Json.Nodes;

namespace Penumbra.CrashHandler.Buffers;

/// <summary> Only expose the write interface for the buffer. </summary>
public interface ICharacterBaseBufferWriter
{
    /// <summary> Write a line into the buffer with the given data. </summary>
    /// <param name="characterAddress"> The address of the related character, if known. </param>
    /// <param name="characterName"> The name of the related character, anonymized or relying on index if unavailable, if known. </param>
    /// <param name="collectionName"> The name of the associated collection. Not anonymized. </param>
    public void WriteLine(nint characterAddress, ReadOnlySpan<byte> characterName, string collectionName);
}

/// <summary> The full crash entry for a loaded character base. </summary>
public record struct CharacterLoadedEntry(
    double Age,
    DateTimeOffset Timestamp,
    int ThreadId,
    string CharacterName,
    string CharacterAddress,
    string CollectionName) : ICrashDataEntry;

internal sealed class CharacterBaseBuffer : MemoryMappedBuffer, ICharacterBaseBufferWriter, IBufferReader
{
    private const int _version = 1;
    private const int _lineCount = 10;
    private const int _lineCapacity = 256;
    private const string _name = "Penumbra.CharacterBase";

    public void WriteLine(nint characterAddress, ReadOnlySpan<byte> characterName, string collectionName)
    {
        var accessor = GetCurrentLineLocking();
        lock (accessor)
        {
            accessor.Write(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            accessor.Write(8, Environment.CurrentManagedThreadId);
            accessor.Write(12, characterAddress);
            var span = GetSpan(accessor, 20, 108);
            WriteSpan(characterName, span);
            span = GetSpan(accessor, 128);
            WriteString(collectionName, span);
        }
    }

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
            var collectionName = ReadString(line[128..]);
            yield return new JsonObject
            {
                [nameof(CharacterLoadedEntry.Age)] = (crashTime - timestamp).TotalSeconds,
                [nameof(CharacterLoadedEntry.Timestamp)] = timestamp,
                [nameof(CharacterLoadedEntry.ThreadId)] = thread,
                [nameof(CharacterLoadedEntry.CharacterName)] = characterName,
                [nameof(CharacterLoadedEntry.CharacterAddress)] = address.ToString("X"),
                [nameof(CharacterLoadedEntry.CollectionName)] = collectionName,
            };
        }
    }

    public uint TotalCount
        => TotalWrittenLines;

    public static IBufferReader CreateReader()
        => new CharacterBaseBuffer(false);

    public static ICharacterBaseBufferWriter CreateWriter()
        => new CharacterBaseBuffer();

    private CharacterBaseBuffer(bool writer)
        : base(_name, _version)
    { }

    private CharacterBaseBuffer()
        : base(_name, _version, _lineCount, _lineCapacity)
    { }
}
