using System;

namespace GostEditor.Core.DocumentModel;

public interface IDocumentNode
{
    DocumentNodeId Id { get; }
}

public abstract class DocumentNode : IDocumentNode
{
    protected DocumentNode()
        : this(DocumentNodeId.New())
    {
    }

    protected DocumentNode(DocumentNodeId id)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException(
                "Идентификатор узла документа не может быть пустым.",
                nameof(id));
        }

        Id = id;
    }

    public DocumentNodeId Id { get; }
}
