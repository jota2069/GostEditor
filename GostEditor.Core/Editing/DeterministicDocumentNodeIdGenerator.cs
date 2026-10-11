using System.Buffers.Binary;
using System.Security.Cryptography;
using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Editing;

internal sealed class DeterministicDocumentNodeIdGenerator
{
    private readonly Guid _operationId;
    private long _sequence;

    public DeterministicDocumentNodeIdGenerator(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Operation ID не может быть пустым.",
                nameof(operationId));
        }

        _operationId = operationId;
    }

    public DocumentNodeId Next()
    {
        Span<byte> source = stackalloc byte[24];
        _operationId.TryWriteBytes(source);
        BinaryPrimitives.WriteInt64LittleEndian(
            source[16..],
            checked(_sequence++));
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(source, hash);
        hash[0] |= 1;
        return new DocumentNodeId(new Guid(hash[..16]));
    }
}
