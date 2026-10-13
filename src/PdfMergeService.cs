using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace SimplePdf
{
    sealed class MergeException : Exception
    {
        public readonly string MessageKey;
        public readonly string FileName;
        public MergeException(string key, string fileName, Exception inner = null) : base(key, inner)
        { MessageKey = key; FileName = fileName; }
    }

    sealed class MergeResult
    {
        public readonly string Path;
        public readonly int PageCount;
        public MergeResult(string path, int pageCount) { Path = path; PageCount = pageCount; }
    }

    static class PdfMergeService
    {
        public static MergeResult Merge(IList<string> inputPaths, string outputPath,
            CancellationToken cancellation, IProgress<int> progress = null)
        {
            if (inputPaths == null || inputPaths.Count < 2) throw new MergeException("mergeNeedTwo", "");
            string destination = System.IO.Path.GetFullPath(outputPath);
            if (!String.Equals(System.IO.Path.GetExtension(destination), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new MergeException("mergeOutputPdf", "");
            // A new destination plus an atomic move prevents overwriting any original or existing file.
            if (File.Exists(destination)) throw new MergeException("mergeOutputExists", "");
            string directory = System.IO.Path.GetDirectoryName(destination);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            string temporary = System.IO.Path.Combine(directory, ".simple-pdf-" + Guid.NewGuid().ToString("N") + ".tmp");
            var documents = new List<PdfDocument>();
            try
            {
                using (var merged = new PdfDocument())
                {
                    merged.Info.Title = System.IO.Path.GetFileNameWithoutExtension(destination);
                    merged.Info.Creator = "Simple-PDF";
                    for (int index = 0; index < inputPaths.Count; index++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string input = System.IO.Path.GetFullPath(inputPaths[index]);
                        PdfDocument source;
                        try { source = PdfReader.Open(input, PdfDocumentOpenMode.Import); }
                        catch (Exception ex) { throw new MergeException("mergeReadFailed", System.IO.Path.GetFileName(input), ex); }
                        documents.Add(source);
                        if (source.SecuritySettings.IsEncrypted)
                            throw new MergeException("mergeEncrypted", System.IO.Path.GetFileName(input));
                        if (source.PageCount == 0) throw new MergeException("mergeReadFailed", System.IO.Path.GetFileName(input));
                        try
                        {
                            for (int page = 0; page < source.PageCount; page++)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                merged.AddPage(source.Pages[page]);
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { throw new MergeException("mergeReadFailed", System.IO.Path.GetFileName(input), ex); }
                        if (progress != null) progress.Report(index + 1);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    int pageCount = merged.PageCount;
                    merged.Save(temporary);
                    cancellation.ThrowIfCancellationRequested();
                    File.Move(temporary, destination);
                    return new MergeResult(destination, pageCount);
                }
            }
            finally
            {
                foreach (var document in documents) document.Dispose();
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
