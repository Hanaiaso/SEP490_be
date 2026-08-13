using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Options;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class DocumentIntelligenceOcrService : IOcrService
    {
        private readonly Lazy<DocumentIntelligenceClient> _client;

        public DocumentIntelligenceOcrService(IOptions<AzureFormRecognizerSettings> options)
        {
            _client = new Lazy<DocumentIntelligenceClient>(() =>
            {
                var settings = options.Value;
                if (string.IsNullOrWhiteSpace(settings.Endpoint) || string.IsNullOrWhiteSpace(settings.ApiKey))
                    throw new InvalidOperationException(
                        "Azure Document Intelligence chưa được cấu hình. Kiểm tra AzureFormRecognizer:Endpoint và AzureFormRecognizer:ApiKey.");

                return new DocumentIntelligenceClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey));
            });
        }

        public async Task<InvoiceOcrResult> ExtractInvoiceAsync(Stream fileStream, string? contentType)
        {
            var content = await BinaryData.FromStreamAsync(fileStream);
            var operation = await _client.Value.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-invoice", content);
            var analyzeResult = operation.Value;

            var result = new InvoiceOcrResult();
            var document = analyzeResult.Documents.FirstOrDefault();
            if (document == null) return result;

            if (document.Fields.TryGetValue("VendorName", out var vendorField))
                result.VendorName = vendorField.ValueString ?? vendorField.Content;

            if (document.Fields.TryGetValue("DueDate", out var dueDateField) && dueDateField.ValueDate.HasValue)
                result.DueDate = dueDateField.ValueDate.Value.DateTime;

            if (document.Fields.TryGetValue("Items", out var itemsField) && itemsField.ValueList != null)
            {
                foreach (var itemField in itemsField.ValueList)
                {
                    if (itemField.ValueDictionary == null) continue;

                    var item = new InvoiceOcrItem();

                    if (itemField.ValueDictionary.TryGetValue("Description", out var descField))
                        item.Description = (descField.ValueString ?? descField.Content ?? string.Empty).Trim();

                    if (itemField.ValueDictionary.TryGetValue("Quantity", out var qtyField) && qtyField.ValueDouble.HasValue)
                        item.Quantity = (decimal)qtyField.ValueDouble.Value;

                    if (itemField.ValueDictionary.TryGetValue("UnitPrice", out var priceField) && priceField.ValueCurrency != null)
                        item.UnitPrice = (decimal)priceField.ValueCurrency.Amount;

                    if (!string.IsNullOrWhiteSpace(item.Description))
                        result.Items.Add(item);
                }
            }

            return result;
        }
    }

    public class AzureFormRecognizerSettings
    {
        public string Endpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}
