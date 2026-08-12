using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ShippingPortal.Api.Services;

public record EstimateItemLine(string ModelProduct, decimal Qty, string? Unit);
public record EstimateChargeLine(string ChargeType, decimal ValueSdg);

public record ClearanceEstimatePrintData(
    string BusinessUnit, string BlAwbNo, string Consignee, string Category,
    List<EstimateItemLine> Items, List<EstimateChargeLine> Charges);

// Replicates CTC Group's official "Clearance Estimate" letterhead template
// (Assets/ctc-letterhead.jpeg + Assets/ctc-70years-badge.png, extracted
// directly from the provided .docx) with live shipment data filled in.
public class ClearanceEstimatePdfService
{
    private static byte[] LoadEmbeddedAsset(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        // Look up the real resource name rather than guessing the
        // project's root namespace — MSBuild derives it independently
        // of the assembly's own file name, and the two don't always
        // match (e.g. hyphens vs. underscores).
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".Assets.{fileName}", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Embedded asset not found: {fileName}. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public byte[] Generate(ClearanceEstimatePrintData data)
    {
        var letterhead = LoadEmbeddedAsset("ctc-letterhead.jpeg");
        var badge = LoadEmbeddedAsset("ctc-70years-badge.png");
        var green = "#008001";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontSize(11));

                // Green left-edge sidebar, matching the original letterhead.
                page.Background().Row(row =>
                {
                    row.ConstantItem(14).Background(green);
                    row.RelativeItem();
                });

                page.Content().PaddingLeft(40).PaddingRight(40).PaddingVertical(24).Column(col =>
                {
                    // Letterhead row: company mark left, anniversary badge right.
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().AlignCenter().Height(70).Image(letterhead).FitHeight();
                        row.ConstantItem(60).AlignRight().Height(50).Image(badge).FitHeight();
                    });

                    col.Item().PaddingTop(24).Text($"Date: {DateTime.UtcNow:dd/MM/yyyy}");
                    ccol.Item().PaddingTop(6).Text($"To: {data.BusinessUnit} Finance Department");

                    col.Item().PaddingTop(28).AlignCenter().Text("Clearance Estimate")
                        .Bold().FontSize(14).Underline();

                    col.Item().PaddingTop(24).Text("Dears,");
                    col.Item().PaddingTop(10).Text("Kindly avail the budget below to allow the clearance of the shipment detailed below:");

                    col.Item().PaddingTop(16).Text($"BL/AWB No.:  {data.BlAwbNo}");
                    col.Item().PaddingTop(4).Text($"Consignee.:  {data.Consignee}");
                    col.Item().PaddingTop(4).Text($"Category.:  {data.Category}");

                    var itemIndex = 1;
                    foreach (var item in data.Items)
                    {
                        col.Item().PaddingTop(4).PaddingLeft(20).Text($"Item-{itemIndex}, {item.ModelProduct}, {item.Qty:N0} {item.Unit}");
                        itemIndex++;
                    }

                    col.Item().PaddingTop(24).Text("Estimate Breakdown:");

                    col.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(40);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Border(1).Padding(5).Text("#").Bold();
                            header.Cell().Border(1).Padding(5).Text("Expense").Bold();
                            header.Cell().Border(1).Padding(5).AlignRight().Text("Amount (SDG)").Bold();
                        });

                        var rowNum = 1;
                        decimal total = 0;
                        foreach (var charge in data.Charges)
                        {
                            table.Cell().Border(1).Padding(5).Text(rowNum.ToString());
                            table.Cell().Border(1).Padding(5).Text(charge.ChargeType);
                            table.Cell().Border(1).Padding(5).AlignRight().Text($"{charge.ValueSdg:N0}");
                            total += charge.ValueSdg;
                            rowNum++;
                        }

                        table.Cell().Border(1).Padding(5).Text("");
                        table.Cell().Border(1).Padding(5).Text("Total").Bold();
                        table.Cell().Border(1).Padding(5).AlignRight().Text($"{total:N0}").Bold();
                    });

                    col.Item().PaddingTop(28).Text("Thank you.");
                    col.Item().PaddingTop(16).Text("Yours truly,");
                    col.Item().PaddingTop(16).Text("Portsudan Branch");
                    col.Item().Text("Finance Department");
                });
            });
        });

        return document.GeneratePdf();
    }
}
