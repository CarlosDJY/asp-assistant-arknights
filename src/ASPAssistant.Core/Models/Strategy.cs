// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
namespace ASPAssistant.Core.Models;

public class Strategy
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initiator { get; set; } = "";
    public string IconPath { get; set; } = "";
    public string Description { get; set; } = "";
    public string UnlockRequirement { get; set; } = "";
    public int InitialHp { get; set; }
    public List<string> InactiveModes { get; set; } = [];
}
