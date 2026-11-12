using ASPAssistant.Core.GameState;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class AcquiredTrackingTests
{
    [Theory]
    [InlineData(TrackingType.Operator)]
    [InlineData(TrackingType.Equipment)]
    public void AcquiredButtonCanToggleRepeatedlyWithoutDuplicatingRows(TrackingType type)
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("重复切换目标", type);
        var shop = new List<ShopItem> { new() { Name = "重复切换目标" } };
        for (var cycle = 0; cycle < 3; cycle++)
        {
            vm.ToggleAcquired("重复切换目标");
            vm.AllEntries.Should().ContainSingle().Which.IsAcquired.Should().BeTrue();
            GameStateUpdater.UpdateShopTracking(shop, vm.AllEntries.ToList());
            shop.Single().IsTracked.Should().BeFalse();
            vm.ToggleAcquired("重复切换目标");
            vm.AllEntries.Should().ContainSingle().Which.IsAcquired.Should().BeFalse();
            GameStateUpdater.UpdateShopTracking(shop, vm.AllEntries.ToList());
            shop.Single().IsTracked.Should().BeTrue();
        }
    }

    [Fact]
    public void AcquiringOperatorRetainsRecordAndRecalculatesOnlyAutomaticRequirements()
    {
        var vm = new TrackingViewModel();
        vm.TrackLineup(new Lineup { Slots = [
            new() { OperatorName = "角峰", IsElite = true, Equipments = ["独占装备", "共享装备", "手动装备"] },
            new() { OperatorName = "隐现", Equipments = ["共享装备"] }
        ] });
        vm.AddTracking("手动装备", TrackingType.Equipment);
        var notifications = 0;
        vm.TrackedOperators.CollectionChanged += (_, _) => notifications++;

        vm.ToggleAcquired("角峰");

        vm.TrackedOperators.Should().HaveCount(2);
        var acquired = vm.TrackedOperators.Single(e => e.Name == "角峰");
        acquired.IsAcquired.Should().BeTrue();
        acquired.IsElite.Should().BeTrue();
        acquired.Equipments.Should().Equal("独占装备", "共享装备", "手动装备");
        vm.IsTracked("角峰").Should().BeFalse();
        vm.IsTracked("隐现").Should().BeTrue();
        vm.TrackedEquipment.Select(e => e.Name).Should().BeEquivalentTo("共享装备", "手动装备");
        notifications.Should().Be(1, "the existing save and overlay subscriptions need a collection notification");

        vm.AddTracking("角峰", TrackingType.Operator);
        vm.TrackedOperators.Should().HaveCount(2);
        vm.IsTracked("角峰").Should().BeTrue();
        vm.TrackedOperators.Single(e => e.Name == "角峰").IsElite.Should().BeTrue();
        vm.IsTracked("独占装备").Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcquiringEquipmentRetainsRecordAndUsesExistingCancellationRules(bool manual)
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        var lineup = new Lineup { Slots = [
            new() { OperatorName = "角峰", Equipments = ["共享装备"] },
            new() { OperatorName = "隐现", Equipments = ["共享装备"] }
        ] };
        vm.TrackLineup(lineup);
        if (manual) vm.AddTracking("共享装备", TrackingType.Equipment);

        vm.ToggleAcquired("共享装备");

        vm.TrackedEquipment.Should().ContainSingle().Which.IsAcquired.Should().BeTrue();
        vm.IsTracked("共享装备").Should().BeFalse();
        vm.TrackedOperators.Should().OnlyContain(e => !e.Equipments.Contains("共享装备"));
        lineup.Slots.Should().OnlyContain(s => s.Equipments.Contains("共享装备"));
        vm.TrackLineup(lineup);
        vm.LoadEntries(vm.AllEntries.ToList());
        vm.TrackedEquipment.Should().ContainSingle().Which.IsAcquired.Should().BeTrue();
        vm.IsTracked("共享装备").Should().BeFalse("automatic refresh must not reactivate an acquired row");

        vm.ToggleAcquired("共享装备");
        vm.TrackedEquipment.Should().ContainSingle().Which.IsManual.Should().BeTrue();
        vm.IsTracked("共享装备").Should().BeTrue();
        vm.RemoveTracking("共享装备");
        vm.TrackedEquipment.Should().BeEmpty();
        vm.TrackedOperators.Should().OnlyContain(e => !e.Equipments.Contains("共享装备"));
    }

    [Fact]
    public void MergeAndPlannedEquipmentChangesDoNotLoseAcquiredState()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        var lineup = new Lineup { Slots = [new() { OperatorName = "角峰", Equipments = ["变形同构体", "炎国短刀"] }] };
        vm.TrackLineup(lineup);
        vm.ToggleAcquired("角峰");
        vm.ClearPlannedTransformation("角峰", "炎国短刀", EquipmentTestData.Rules);
        vm.TrackedOperators.Single().IsAcquired.Should().BeTrue();
        vm.TrackedEquipment.Should().BeEmpty();

        vm.TrackLineup(lineup).Operators.Should().Be(1);
        vm.TrackedOperators.Should().ContainSingle().Which.IsAcquired.Should().BeFalse();
        vm.ToggleAcquired("角峰");
        vm.RemoveTracking("角峰");
        vm.TrackedOperators.Should().BeEmpty();
    }

    [Fact]
    public void CachedShopHighlightsIgnoreAcquiredRowsImmediately()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.AddTracking("盾牌", TrackingType.Equipment);
        vm.AddTracking("隐现", TrackingType.Operator);
        var shop = new List<ShopItem> { new() { Name = "角峰" }, new() { Name = "盾牌" }, new() { Name = "隐现" } };
        GameStateUpdater.UpdateShopTracking(shop, vm.AllEntries.ToList());
        shop.Should().OnlyContain(i => i.IsTracked);
        vm.ToggleAcquired("角峰");
        vm.ToggleAcquired("盾牌");
        GameStateUpdater.UpdateShopTracking(shop, vm.AllEntries.ToList());
        shop.Where(i => i.IsTracked).Select(i => i.Name).Should().Equal("隐现");
        vm.AllEntries.Should().HaveCount(3);
    }
}
