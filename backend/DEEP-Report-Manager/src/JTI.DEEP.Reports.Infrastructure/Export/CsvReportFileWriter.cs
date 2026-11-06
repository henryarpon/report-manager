using System.Globalization;
using System.IO.Compression;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using JTI.DEEP.Reports.Application.Reports.Abstractions;

namespace JTI.DEEP.Reports.Infrastructure.Export;

/// <summary>
/// Streams rows to CSV with CsvHelper. Quoting/escaping of commas, quotes and newlines is handled by
/// CsvHelper; cells starting with =, +, -, @, tab or CR are escaped against formula injection.
/// All writes are async (Kestrel disallows synchronous I/O on the response body).
/// </summary>
public sealed class CsvReportFileWriter : IReportFileWriter
{
    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        InjectionOptions = InjectionOptions.Escape,
    };

    // UTF-8 with BOM so Excel shows names with ñ and other accents correctly.
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public async Task WriteCsvAsync(Stream output, IReadOnlyList<string> headers, IAsyncEnumerable<object?[]> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(output, Utf8WithBom, bufferSize: 64 * 1024, leaveOpen: true);
        await using var csv = new CsvWriter(writer, Config, leaveOpen: true);

        await WriteHeaderAsync(csv, headers);
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await WriteRowAsync(csv, row);
        }

        await csv.FlushAsync();
        await writer.FlushAsync(cancellationToken);
    }

    public async Task WriteZipAsync(Stream output, IReadOnlyList<string> headers, IAsyncEnumerable<object?[]> rows,
        int rowsPerFile, string partFilePrefix, CancellationToken cancellationToken)
    {
        await using var drain = new AsyncDrainStream(output);
        await using var enumerator = rows.GetAsyncEnumerator(cancellationToken);

        // ZipArchive writes synchronously into the in-memory drain; we push it to the response asynchronously.
        var zip = new ZipArchive(drain, ZipArchiveMode.Create, leaveOpen: true);
        try
        {
            var hasRow = await enumerator.MoveNextAsync();
            var part = 0;
            do
            {
                part++;
                var entry = zip.CreateEntry($"{partFilePrefix}{part}.csv", CompressionLevel.Fastest);
                var entryStream = entry.Open();
                var writer = new StreamWriter(entryStream, Utf8WithBom, bufferSize: 64 * 1024, leaveOpen: true);
                var csv = new CsvWriter(writer, Config, leaveOpen: true);

                await WriteHeaderAsync(csv, headers);
                for (var written = 0; hasRow && written < rowsPerFile; written++)
                {
                    await WriteRowAsync(csv, enumerator.Current);
                    hasRow = await enumerator.MoveNextAsync();

                    if (written % DrainEveryRows == 0)
                    {
                        await writer.FlushAsync(cancellationToken);
                        await drain.DrainAsync(cancellationToken);
                    }
                }

                // Close the entry (writes its trailer into the drain) before starting the next part.
                await csv.FlushAsync();
                csv.Dispose();
                writer.Dispose();
                entryStream.Dispose();
                await drain.DrainAsync(cancellationToken);
            }
            while (hasRow);
        }
        finally
        {
            zip.Dispose(); // writes the central directory into the drain
        }

        await drain.DrainAsync(cancellationToken);
    }

    private const int DrainEveryRows = 1_000;

    private static async Task WriteHeaderAsync(CsvWriter csv, IReadOnlyList<string> headers)
    {
        foreach (var header in headers)
        {
            csv.WriteField(header);
        }

        await csv.NextRecordAsync();
    }

    private static async Task WriteRowAsync(CsvWriter csv, object?[] row)
    {
        foreach (var value in row)
        {
            csv.WriteField(FormatCell(value));
        }

        await csv.NextRecordAsync();
    }

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
