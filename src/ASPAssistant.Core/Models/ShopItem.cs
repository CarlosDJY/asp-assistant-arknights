// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
namespace ASPAssistant.Core.Models;

public class ShopItem
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Unique key for this card instance, used to identify it across the
    /// stabilizer and overlay pool. The reader uses Name@occurrence, left to right,
    /// so duplicate names remain separate and OCR pixel jitter does not change IDs.
    /// </summary>
    public string Id { get; set; } = "";

    public int Price { get; set; }
    public bool IsTracked { get; set; }
    /// <summary>Tracked only as a 过渡 (transition) operator of an imported route.</summary>
    public bool IsTransition { get; set; }
    /// <summary>Detected name bounds, normalized to the full captured client image.</summary>
    public (double X, double Y, double Width, double Height) OcrRegion { get; set; }
}
