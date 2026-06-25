using Lite.Interaction;

namespace Lite.Scripting.Dom;

/// <summary>The File interface (a Blob with name/lastModified) exposed to JavaScript.
/// Binary content is not modeled — <see cref="SelectedFile.TextContent"/> backs text reads.</summary>
public class JsFile
{
    private readonly SelectedFile _file;
    internal JsFile(SelectedFile file) => _file = file;

    public string name => _file.Name;
    public double size => _file.Size;
    public string type => _file.Type;
    public double lastModified => _file.LastModified;

    /// <summary>Blob.text() — resolves to the file's text content. (Returned synchronously here;
    /// the spec returns a Promise, but the value is what callers assert on.)</summary>
    public string text() => _file.TextContent;
}

/// <summary>The FileList returned by HTMLInputElement.files (an ordered, indexable collection).</summary>
public class JsFileList
{
    private readonly IReadOnlyList<SelectedFile> _files;
    internal JsFileList(IReadOnlyList<SelectedFile> files) => _files = files;

    public int length => _files.Count;

    public JsFile? item(int index) =>
        index >= 0 && index < _files.Count ? new JsFile(_files[index]) : null;

    /// <summary>Indexer so files[i] works from JS.</summary>
    public JsFile? this[int index] => item(index);
}
