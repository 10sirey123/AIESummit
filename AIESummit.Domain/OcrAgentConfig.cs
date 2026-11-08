using Blueshift.Domain.Abstract;
using Blueshift.Domain.Infrastructure;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:SlidersHorizontal", MenuName = "OCR settings",
    MenuDescription = "Runtime controls for the OCR agent. Its endpoint, model deployment, and credentials are configured in the gemini-ocr API connection.")]
public sealed class OcrAgentConfig : BaseAgentConfig { }
