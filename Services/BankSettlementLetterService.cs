using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ShippingPortal.Api.Services;

public record LetterLineItem(string CollectionRefNo, string Category, string InvoiceNo, decimal ValueAed, decimal PaidAed, decimal RemainingAed, decimal PaymentRequest);

// Generates the "group settlement letter" as a plain Word document —
// deliberately no letterhead, so it can be copied straight onto the
// company's own pre-headed paper, per direct request. A .docx (not
// PDF) so the recipient can paste and adjust it before printing.
public class BankSettlementLetterService
{
    public byte[] Generate(string receiverBankAddress, string accountNo,
        string senderBankName, decimal totalAed, List<LetterLineItem> lines)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            body.AppendChild(Para($"Date: {DateTime.UtcNow:dd MMMM yyyy}"));
            body.AppendChild(Para(""));

            // Receiver Bank's address, one paragraph per line — preserves
            // however many lines it was entered with in Settings.
            body.AppendChild(Para("To: General Manager"));
            foreach (var line in receiverBankAddress.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                body.AppendChild(Para(line));
            body.AppendChild(Para(""));

            body.AppendChild(Para("Dear Sirs,", bold: true));
            body.AppendChild(Para(""));

            body.AppendChild(Para(
                $"Please find below the details of the amounts to be deducted from our import account No. {accountNo}, " +
                $"totaling AED {totalAed:N2}, in settlement of the collections listed below."));
            body.AppendChild(Para(""));

            body.AppendChild(BuildTable(lines));
            body.AppendChild(Para(""));

            body.AppendChild(Para($"As collection instructions received from {senderBankName}."));
            body.AppendChild(Para(""));
            body.AppendChild(Para(""));
            body.AppendChild(Para("Best regards,"));
        }
        return ms.ToArray();
    }

    private static Paragraph Para(string text, bool bold = false)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        if (bold) run.RunProperties = new RunProperties(new Bold());
        return new Paragraph(run);
    }

    private static Table BuildTable(List<LetterLineItem> lines)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 6 },
                new BottomBorder { Val = BorderValues.Single, Size = 6 },
                new LeftBorder { Val = BorderValues.Single, Size = 6 },
                new RightBorder { Val = BorderValues.Single, Size = 6 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 }
            )
        ));

        string[] headers = { "Collection Ref. No.", "Category", "Invoice No.", "Collection Value (AED)", "Settled (AED)", "Remaining (AED)", "Payment Request (AED)" };
        var headerRow = new TableRow();
        foreach (var h in headers) headerRow.AppendChild(Cell(h, bold: true));
        table.AppendChild(headerRow);

        foreach (var l in lines)
        {
            var row = new TableRow();
            row.AppendChild(Cell(l.CollectionRefNo));
            row.AppendChild(Cell(l.Category));
            row.AppendChild(Cell(l.InvoiceNo));
            row.AppendChild(Cell(l.ValueAed.ToString("N2")));
            row.AppendChild(Cell(l.PaidAed.ToString("N2")));
            row.AppendChild(Cell(l.RemainingAed.ToString("N2")));
            row.AppendChild(Cell(l.PaymentRequest.ToString("N2")));
            table.AppendChild(row);
        }

        // Totals row
        var totalRow = new TableRow();
        totalRow.AppendChild(Cell("Total", bold: true));
        totalRow.AppendChild(Cell(""));
        totalRow.AppendChild(Cell(""));
        totalRow.AppendChild(Cell(""));
        totalRow.AppendChild(Cell(""));
        totalRow.AppendChild(Cell(""));
        totalRow.AppendChild(Cell(lines.Sum(l => l.PaymentRequest).ToString("N2"), bold: true));
        table.AppendChild(totalRow);

        return table;
    }

    private static TableCell Cell(string text, bool bold = false)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        if (bold) run.RunProperties = new RunProperties(new Bold());
        return new TableCell(new Paragraph(run));
    }
}
