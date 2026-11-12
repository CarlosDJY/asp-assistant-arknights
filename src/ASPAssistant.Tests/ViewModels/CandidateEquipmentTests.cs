// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class CandidateEquipmentTests
{
    private static LineupEditorViewModel Editor(Lineup source)
    {
        Operator[] operators = [new() { Name = "冻河", CoreCovenants = ["谢拉格"] }, new() { Name = "角峰", CoreCovenants = ["谢拉格"] }];
        return new LineupEditorViewModel(source, operators, [],
            new CovenantCalculator(operators, [new() { Name = "谢拉格", BondType = "SEASON", ActivateCount = 1 }],
                new Dictionary<string, string>()));
    }

    private static Lineup Source() => new()
    {
        Name = "候选",
        Slots =
        [
            new() { OperatorName = "冻河", Equipments = ["坚守盾牌"], CoreEquipments = ["坚守盾牌"],
                CandidateEquipments = [new() { Name = "歌利亚头盔", IsPriority = true }, new() { Name = "不退者之盾" }] },
            new() { OperatorName = "角峰", CandidateEquipments = [new() { Name = "不退者之盾" }, new() { Name = "坚守盾牌" }] },
        ]
    };

    [Fact]
    public void OldLineupsHaveNoCandidates_AndRoundTripKeepsThem()
    {
        var old = JsonSerializer.Deserialize<LineupSlot>("""{"operatorName":"冻河","equipments":["坚守盾牌"]}""")!;
        old.CandidateEquipments.Should().BeEmpty();

        var json = JsonSerializer.Serialize(Source());
        json.Should().Contain("\"candidateEquipments\"").And.Contain("\"isPriority\":true");
        var back = JsonSerializer.Deserialize<Lineup>(json)!;
        back.Slots[0].CandidateEquipments.Select(c => (c.Name, c.IsPriority))
            .Should().Equal(("歌利亚头盔", true), ("不退者之盾", false));
    }

    [Fact]
    public void EditorKeepsCandidatesOutOfTheSlots()
    {
        var vm = Editor(Source());
        vm.SetSlotCandidates(0, [new() { Name = "坚守盾牌" }, new() { Name = "护盾无人机" }, new() { Name = "护盾无人机", IsPriority = true }]);
        vm.Slots[0].CandidateEquipments.Select(c => c.Name).Should().Equal("护盾无人机");

        vm.ToggleCandidatePriority(0, "护盾无人机");
        vm.Slots[0].CandidateEquipments.Single().IsPriority.Should().BeTrue();

        vm.MoveCandidateToSlot(0, "护盾无人机");
        vm.Slots[0].Equipments.Should().Equal("坚守盾牌", "护盾无人机");
        vm.Slots[0].CandidateEquipments.Should().BeEmpty();
        var full = () => vm.MoveCandidateToSlot(0, "M3茧甲");
        full.Should().Throw<InvalidOperationException>();

        vm.SetSlotCandidates(0, [new() { Name = "M3茧甲" }]);
        vm.RemoveCandidate(0, "M3茧甲");
        vm.Slots[0].CandidateEquipments.Should().BeEmpty();
        vm.ToLineup().Slots[0].Equipments.Should().Equal("坚守盾牌", "护盾无人机");
    }

    [Fact]
    public void TrackingTakesTheStrongestReason_AndForgetsCandidatesOfUntrackedOperators()
    {
        var vm = new TrackingViewModel();
        vm.TrackLineup(Source());
        var kinds = vm.TrackedEquipment.ToDictionary(e => e.Name, e => e.EquipmentKind);
        kinds.Should().BeEquivalentTo(new Dictionary<string, TrackedEquipmentKind>
        {
            ["坚守盾牌"] = TrackedEquipmentKind.Planned,   // 冻河's slot beats 角峰's candidate
            ["歌利亚头盔"] = TrackedEquipmentKind.Priority,
            ["不退者之盾"] = TrackedEquipmentKind.Candidate,
        });
        vm.EquipmentWanters("不退者之盾").Should().Equal(("冻河", TrackedEquipmentKind.Candidate), ("角峰", TrackedEquipmentKind.Candidate));
        vm.IsTracked("歌利亚头盔").Should().BeTrue();

        vm.RemoveTracking("冻河");
        vm.TrackedEquipment.Select(e => (e.Name, e.EquipmentKind)).Should().BeEquivalentTo(new[]
        {
            ("不退者之盾", TrackedEquipmentKind.Candidate), ("坚守盾牌", TrackedEquipmentKind.Candidate),
        });
    }

    [Fact]
    public void AcquiringACandidateDropsItFromEveryOperator()
    {
        var vm = new TrackingViewModel();
        vm.TrackLineup(Source());
        vm.ToggleAcquired("不退者之盾");
        vm.TrackedOperators.Should().OnlyContain(e => e.CandidateEquipments.All(c => c.Name != "不退者之盾"));
        vm.TrackedEquipment.Single(e => e.Name == "不退者之盾").IsAcquired.Should().BeTrue();
        vm.IsTracked("不退者之盾").Should().BeFalse();
    }
}
