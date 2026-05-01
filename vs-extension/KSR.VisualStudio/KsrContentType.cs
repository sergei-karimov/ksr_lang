using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace KSR.VisualStudio;

internal static class KsrContentType
{
    public const string Name = "ksr";

    [Export]
    [Name(Name)]
    [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    internal static ContentTypeDefinition? KsrContentTypeDefinition = null;

    [Export]
    [ContentType(Name)]
    [FileExtension(".ksr")]
    internal static FileExtensionToContentTypeDefinition? KsrFileExtensionDefinition = null;
}
