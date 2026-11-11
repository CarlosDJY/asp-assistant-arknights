// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
namespace ASPAssistant.Core.GameModes.GarrisonProtocol;

public class GarrisonOcrStrategy : IOcrStrategy
{
    private static readonly List<OcrRegionDefinition> Regions =
    [
        new("Shop", XPercent: 0.00, YPercent: 0.50, WidthPercent: 1.00, HeightPercent: 0.50),
        new("Gold", XPercent: 0.85, YPercent: 0.02, WidthPercent: 0.12, HeightPercent: 0.05),
        new("Round", XPercent: 0.45, YPercent: 0.02, WidthPercent: 0.10, HeightPercent: 0.05),
        new("Field", XPercent: 0.10, YPercent: 0.72, WidthPercent: 0.80, HeightPercent: 0.15),
        new("Bench", XPercent: 0.10, YPercent: 0.88, WidthPercent: 0.80, HeightPercent: 0.10),
        // Regulation: enemy leader and training types are in the right-hand panel.
        new("EnemySectionScan", XPercent: 0.68, YPercent: 0.40, WidthPercent: 0.30, HeightPercent: 0.40),
        // Ban screen detection: scan the header area for "确认本局信息".
        new("BanConfirmText", XPercent: 0.25, YPercent: 0.00, WidthPercent: 0.50, HeightPercent: 0.10),
        // Ban screen: scan the left faction panel for covenant OCR pre-filter.
        new("BanFactionPanel", XPercent: 0.05, YPercent: 0.00, WidthPercent: 0.25, HeightPercent: 0.90),
        // Ban screen: the covenant panels are read from ONE un-scrolled screenshot.
        // Broad search bands include the titles and both icon-name rows. The reader
        // bounds the grid by the detected titles, including 本局禁用干员 when visible.
        new("BanCoreSection", XPercent: 0.00, YPercent: 0.16, WidthPercent: 0.70, HeightPercent: 0.24),
        // The 100% UI puts second-row labels below 85% of the client height.
        // The reader bounds the grid by the detected section titles.
        new("BanAdditionalSection", XPercent: 0.00, YPercent: 0.43, WidthPercent: 0.70, HeightPercent: 0.57),
    ];

    public IReadOnlyList<OcrRegionDefinition> GetScanRegions() => Regions;
}
