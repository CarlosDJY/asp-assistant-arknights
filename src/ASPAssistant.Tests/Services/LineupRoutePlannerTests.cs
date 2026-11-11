using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class LineupRoutePlannerTests
{
    private static CovenantInfo Core(string name) => new() { Name = name, BondType = "SEASON", ActivateCount = 3 };
    private static CovenantInfo Extra(string name, int count = 2) => new() { Name = name, BondType = "REGULAR", ActivateCount = count };
    private static Operator Op(string name, int tier, string[]? core = null, string[]? extra = null)
        => new() { Name = name, Tier = tier, CoreCovenants = [.. core ?? []], AdditionalCovenants = [.. extra ?? []] };

    private static readonly CovenantInfo[] Covenants = [Core("炎"), Core("维多利亚"), Extra("精准"), Extra("突袭")];

    /// <summary>Final lineup is high tier; low-tier 炎 holders exist only as transition cards.</summary>
    private static readonly Operator[] Operators =
    [
        Op("炎高1", 4, ["炎"]), Op("炎高2", 5, ["炎"]), Op("炎高3", 5, ["炎"]), Op("炎高4", 6, ["炎"]),
        Op("炎高5", 4, ["炎"], ["精准"]), Op("炎高6", 3, ["炎"], ["精准"]),
        Op("精准高", 4, extra: ["精准"]), Op("精准低", 2, extra: ["精准"]),
        Op("炎低1", 1, ["炎"]), Op("炎低2", 1, ["炎"]), Op("炎低3", 2, ["炎"]), Op("炎低4", 2, ["炎"]),
        Op("维低1", 1, ["维多利亚"]), Op("维低2", 2, ["维多利亚"], ["突袭"]), Op("杂1", 1, extra: ["突袭"]), Op("杂2", 3),
    ];

    private static readonly PlannerGoal[] Goals = [new("炎", 2, 6, false, true), new("精准", 2, 3, false, false)];

    private static PlannedLineup Final()
    {
        string[] names = ["炎高1", "炎高2", "炎高3", "炎高4", "炎高5", "炎高6", "精准高", "精准低"];
        return new PlannedLineup(8, 0, names.Select(n => new PlannedOperator(n, null, [])).ToList(),
            new Dictionary<string, int>(), false) { Goals = Goals };
    }

    private static PlannedRoute Plan(IReadOnlySet<string>? bannedOperators = null)
        => new LineupRoutePlanner(Operators, Covenants).Plan(new RouteRequest
        {
            Final = Final(), Goals = Goals,
            BannedOperators = bannedOperators ?? new HashSet<string>()
        });

    [Fact]
    public void Route_RespectsTerminalTierAndTripleSlots()
    {
        var route = Plan();
        var tiers = Operators.ToDictionary(o => o.Name, o => o.Tier);

        route.Stages.Select(s => s.Definition.Name).Should().Equal("开局", "前期", "中期", "后期", "大成（打 Boss）");
        foreach (var stage in route.Stages.Where(s => !s.IsFinal))
        {
            var def = stage.Definition;
            stage.Operators.Should().HaveCount(def.People);
            stage.Operators.Should().OnlyContain(o => tiers[o.Name] <= def.MaxTier + 1);
            stage.Operators.Count(o => tiers[o.Name] > def.MaxTier).Should().BeLessThanOrEqualTo(def.TripleSlots);
            stage.Operators.Where(o => o.ViaTriple).Should().OnlyContain(o => tiers[o.Name] == def.MaxTier + 1);
        }
        route.Stages[^1].IsFinal.Should().BeTrue();
        route.Stages[^1].Operators.Select(o => o.Name).Should().BeEquivalentTo(Final().Operators.Select(o => o.Name));
    }

    [Fact]
    public void Route_MarksFinalAndTransitionCards()
    {
        var route = Plan();
        var final = Final().Operators.Select(o => o.Name).ToHashSet();

        route.Stages.SelectMany(s => s.Operators).Should().OnlyContain(o =>
            (o.Role == RouteOperatorRole.Final) == final.Contains(o.Name));
        route.Stages[0].Operators.Should().Contain(o => o.Role == RouteOperatorRole.Transition);
        route.Stages[0].Operators.Should().OnlyContain(o => o.IsNew);
    }

    [Fact]
    public void Route_ActivatesTheCoreGoalEarlyWithTransitionCards()
    {
        var route = Plan();

        var opening = route.Stages[0];
        opening.Covenants.Single(c => c.Name == "炎").Level.Should().BeGreaterThanOrEqualTo(1);
        // Later stages keep building towards the target.
        route.Stages.Select(s => s.Covenants.Single(c => c.Name == "炎").Count).Should().BeInAscendingOrder();
    }

    [Fact]
    public void StackingStage_KeepsTargetCoreAtPlanAndOneAdditionalAtPlan()
    {
        var route = Plan();
        var stacking = route.Stages.Single(s => s.Definition.Stacking);

        stacking.Covenants.Single(c => c.Name == "炎").Count.Should().BeGreaterThanOrEqualTo(6);
        stacking.Covenants.Single(c => c.Name == "精准").Count.Should().BeGreaterThanOrEqualTo(3);
        stacking.Summary!.Warnings.Should().NotContain(w => w.Contains("未达规划") || w.Contains("小盟约"));
    }

    [Fact]
    public void Route_NeverUsesBannedOperatorsAndReportsReplacements()
    {
        var route = Plan(new HashSet<string> { "炎低1", "炎低2" });

        route.Stages.SelectMany(s => s.Operators).Should().NotContain(o => o.Name == "炎低1" || o.Name == "炎低2");
        for (var i = 1; i < route.Stages.Count; i++)
        {
            var previous = route.Stages[i - 1].Operators.Select(o => o.Name).ToHashSet();
            var current = route.Stages[i].Operators.Select(o => o.Name).ToHashSet();
            route.Stages[i].Removed.Should().BeEquivalentTo(previous.Except(current));
            route.Stages[i].Operators.Where(o => o.IsNew).Select(o => o.Name).Should().BeEquivalentTo(current.Except(previous));
        }
    }

    private static Operator WithProfile(Operator op, string profession, OperatorStackInfo? stack = null)
    {
        op.Profile = new OperatorProfile
        {
            Profession = profession,
            Roles = profession switch { "重装" => [OperatorProfile.TankRole], "医疗" => [OperatorProfile.HealerRole], _ => [] },
            Stack = stack
        };
        return op;
    }

    [Fact]
    public void Route_KeepsATankEverywhereAndAHealerFromTerminalThree()
    {
        var ops = new List<Operator>
        {
            WithProfile(Op("盾1", 1, ["炎"]), "重装"), WithProfile(Op("盾2", 3), "重装"),
            WithProfile(Op("奶1", 2), "医疗"), WithProfile(Op("奶2", 4), "医疗"),
        };
        for (var i = 0; i < 8; i++) ops.Add(WithProfile(Op($"炎{i}", 1 + i % 4, ["炎"]), "近卫"));
        for (var i = 0; i < 6; i++) ops.Add(WithProfile(Op($"杂{i}", 1 + i % 4, ["维多利亚"]), "狙击"));
        var final = new PlannedLineup(8, 0, new[] { "炎0", "炎1", "炎2", "炎3", "炎4", "炎5", "盾2", "奶2" }
            .Select(n => new PlannedOperator(n, null, [])).ToList(), new Dictionary<string, int>(), false) { Goals = [new("炎", 2, 6, false, true)] };

        var route = new LineupRoutePlanner(ops, Covenants).Plan(new RouteRequest { Final = final });

        foreach (var stage in route.Stages)
        {
            stage.Operators.Should().Contain(o => o.IsTank, stage.Definition.Name);
            if (stage.Definition.TerminalLevel >= LineupRoutePlanner.HealerFromTerminal)
                stage.Operators.Should().Contain(o => o.IsHealer, stage.Definition.Name);
            stage.Summary.Warnings.Should().BeEmpty();
        }
    }

    [Fact]
    public void Route_PrefersAStackerOverAPlainFillerOfTheSameCovenant()
    {
        var ops = new List<Operator>
        {
            WithProfile(Op("盾", 1), "重装"),
            WithProfile(Op("炎A", 1, ["炎"]), "近卫"), WithProfile(Op("炎B", 1, ["炎"]), "近卫"),
            WithProfile(Op("炎C", 2, ["炎"]), "近卫"),
            WithProfile(Op("叠炎", 1, ["炎"]), "辅助", new OperatorStackInfo { Kind = "covenant", Covenants = ["炎"], PerRound = 8 }),
        };
        var final = new PlannedLineup(4, 0, new[] { "盾", "炎A", "炎B", "炎C" }.Select(n => new PlannedOperator(n, null, [])).ToList(),
            new Dictionary<string, int>(), false) { Goals = [new("炎", 1, 3, false, true)] };

        var route = new LineupRoutePlanner(ops, Covenants).Plan(new RouteRequest
        {
            Final = final,
            Stages = [new RouteStageDefinition("开局", 2, 2, 0, 4)]
        });

        route.Stages[0].Operators.Select(o => o.Name).Should().Contain("叠炎").And.Contain("盾");
        route.Stages[0].Operators.Single(o => o.Name == "叠炎").IsStacker.Should().BeTrue();
    }

    [Fact]
    public void StackLayers_CountsOnlyActiveCovenantsAndIgnoresOnAcquire()
    {
        var stacker = WithProfile(Op("叠炎", 1, ["炎"]), "辅助", new OperatorStackInfo { Kind = "covenant", Covenants = ["炎", "维多利亚"], Amount = 4, PerRound = 4 });
        var once = WithProfile(Op("获得时", 1, ["炎"]), "辅助", new OperatorStackInfo { Kind = "once", Covenants = ["炎"] });
        var team = new List<Operator> { stacker, once, Op("炎1", 1, ["炎"]) };
        var planner = new LineupRoutePlanner(team, Covenants);
        var counts = new Dictionary<string, int> { ["炎"] = 3, ["维多利亚"] = 0 };

        var layers = planner.StackLayers(team, counts, new HashSet<string> { "炎" });

        layers["叠炎"].Final.Should().Be(4);        // 维多利亚 is not active, so only 炎 is stacked
        layers["叠炎"].Transition.Should().Be(0);
        layers["获得时"].Value.Should().Be(0);
        once.Profile!.IsStacker.Should().BeFalse();
    }

    [Fact]
    public void StackLayers_AmplifiersUseTheirTargets()
    {
        var capped = WithProfile(Op("战斗叠层", 4, ["炎"]), "术师", new OperatorStackInfo { Kind = "covenant", Covenants = ["炎"], Amount = 1, Cap = 16, PerRound = 12 });
        var mon3tr = WithProfile(Op("Mon3tr", 5, extra: ["协防干员"]), "医疗", new OperatorStackInfo
            { Kind = "amplifier", Amplify = new OperatorStackAmplify { Type = "capPercent", Value = 0.3, Scope = "around" } });
        var amiya = WithProfile(Op("阿米娅", 5, extra: ["奇迹"]), "术师", new OperatorStackInfo
            { Kind = "amplifier", Amplify = new OperatorStackAmplify { Type = "perEvent", Value = 1, Scope = "front" } });
        var team = new List<Operator> { capped, mon3tr, amiya, Op("炎1", 1, ["炎"]), Op("炎2", 1, ["炎"]) };
        var planner = new LineupRoutePlanner(team, Covenants);
        var counts = new Dictionary<string, int> { ["炎"] = 3 };

        var layers = planner.StackLayers(team, counts, new HashSet<string> { "炎" });

        layers["战斗叠层"].Final.Should().Be(12);
        layers["Mon3tr"].Final.Should().BeApproximately(12 * 0.3, 1e-9); // +30% cap on the capped stacker
        layers["阿米娅"].Final.Should().BeApproximately(12, 1e-9);       // 12 events/round × +1
    }
}
