using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf.IO;

namespace SimplePdf
{
    static class MergeTests
    {
        static string Hash(string path) { using (var algorithm = SHA256.Create()) return Convert.ToBase64String(algorithm.ComputeHash(File.ReadAllBytes(path))); }
        static void ExpectFailure(Action action, string messageKey)
        {
            try { action(); } catch (MergeException ex) { if (ex.MessageKey == messageKey) return; throw; }
            throw new Exception("Expected rejection: " + messageKey);
        }
        static int Main(string[] args)
        {
            try
            {
                string fixtures = args[0];
                string results = args[1];
                Directory.CreateDirectory(results);
                string a = Path.Combine(fixtures, "paper-a.pdf"), b = Path.Combine(fixtures, "paper-b.pdf"), c = Path.Combine(fixtures, "paper-c.pdf");
                string[] inputs = { c, a, b };
                string[] hashes = inputs.Select(Hash).ToArray();
                string output = Path.Combine(results, "merged.pdf");
                if (File.Exists(output)) File.Delete(output);
                var merged = PdfMergeService.Merge(inputs, output, CancellationToken.None);
                using (var document = PdfReader.Open(output, PdfDocumentOpenMode.Import))
                {
                    if (document.PageCount != 4 || merged.PageCount != 4) throw new Exception("Incorrect page count.");
                    string[] expected = { "paper-c.pdf - PAGE 1", "paper-a.pdf - PAGE 1", "paper-a.pdf - PAGE 2", "paper-b.pdf - PAGE 1" };
                    for (int page = 0; page < expected.Length; page++)
                    {
                        string content = Encoding.ASCII.GetString(document.Pages[page].Contents.CreateSingleContent().Stream.UnfilteredValue);
                        if (!content.Contains(expected[page])) throw new Exception("Wrong page order at page " + (page + 1));
                    }
                    if (document.Pages[0].Rotate != 90) throw new Exception("Page rotation was lost.");
                    if (document.Pages[3].Width.Point <= document.Pages[3].Height.Point) throw new Exception("Landscape page size was lost.");
                }
                for (int index = 0; index < inputs.Length; index++) if (Hash(inputs[index]) != hashes[index]) throw new Exception("An input PDF was changed.");
                ExpectFailure(delegate { PdfMergeService.Merge(new[] { a }, Path.Combine(results, "too-few.pdf"), CancellationToken.None); }, "mergeNeedTwo");
                ExpectFailure(delegate { PdfMergeService.Merge(new[] { a, b }, a, CancellationToken.None); }, "mergeOutputExists");
                ExpectFailure(delegate { PdfMergeService.Merge(new[] { a, b }, output, CancellationToken.None); }, "mergeOutputExists");
                ExpectFailure(delegate { PdfMergeService.Merge(new[] { a, b }, Path.Combine(results, "wrong.txt"), CancellationToken.None); }, "mergeOutputPdf");
                ExpectFailure(delegate { PdfMergeService.Merge(new[] { a, Path.Combine(fixtures, "damaged.pdf") }, Path.Combine(results, "bad.pdf"), CancellationToken.None); }, "mergeReadFailed");
                ExpectFailure(delegate { PdfMergeService.Merge(new[] { a, Path.Combine(fixtures, "protected.pdf") }, Path.Combine(results, "protected-output.pdf"), CancellationToken.None); }, "mergeReadFailed");
                using (var cancel = new CancellationTokenSource())
                {
                    var progress = new CancelAfterFirstFile(cancel);
                    try { PdfMergeService.Merge(inputs, Path.Combine(results, "canceled.pdf"), cancel.Token, progress); throw new Exception("Cancellation was ignored."); }
                    catch (OperationCanceledException) { }
                }
                foreach (string name in new[] { "bad.pdf", "protected-output.pdf", "canceled.pdf" })
                    if (File.Exists(Path.Combine(results, name))) throw new Exception("A failed merge left an output file.");
                if (Directory.GetFiles(results, ".simple-pdf-*.tmp").Length != 0) throw new Exception("A merge left temporary files.");
                Console.WriteLine("PASS: merge order fixture, page count, rotation, landscape size, unchanged originals, overwrite protection, corrupt/encrypted PDFs, cancellation, and cleanup.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        sealed class CancelAfterFirstFile : IProgress<int>
        {
            readonly CancellationTokenSource cancellation;
            public CancelAfterFirstFile(CancellationTokenSource source) { cancellation = source; }
            public void Report(int count) { cancellation.Cancel(); }
        }
    }
}
