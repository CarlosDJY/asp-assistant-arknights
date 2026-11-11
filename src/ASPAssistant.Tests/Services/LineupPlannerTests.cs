using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class LineupPlannerTests
{
    [Fact]
    public void Plan_ReportsBranchLimitWhenCandidatesAreSkipped()
    {
        var planner = new LineupPlanner(Enumerable.Range(0, 49).Select(i => Op($"干员{i}", ["炎"])), [Core("炎")]);
        var variant = planner.Plan(new PlannerRequest
        {
            // One slot: every lineup is a single target holder, so only the 48-branch cap limits the search.
            Goals = [new("炎", 1, 1, false, true)], PeopleOptions = [1], TransformOptions = [0]
        }, CancellationToken.None, maxResultsPerVariant: 100).Variants.Single();

        variant.Lineups.Should().HaveCount(48);
        variant.SearchLimited.Should().BeTrue();
        variant.Lineups.Should().OnlyContain(l => l.SearchLimited);
    }

    private static CovenantInfo Core(string name) => new() { Name = name, BondType = "SEASON", ActivateCount = 3 };
    private static CovenantInfo Extra(string name, int count = 2) => new() { Name = name, BondType = "REGULAR", ActivateCount = count };
    private static Operator Op(string name, string[]? core = null, string[]? extra = null)
        => new() { Name = name, CoreCovenants = [.. core ?? []], AdditionalCovenants = [.. extra ?? []], IconPath = $"icons/operators/{name}.png" };

    private static Operator Healer(Operator op)
    {
        op.Profile = new OperatorProfile { Profession = "医疗", Roles = [OperatorProfile.HealerRole] };
        return op;
    }

    [Fact]
    public void Plan_KeepsAtLeastOneHealerInTheFinalLineup()
    {
        var ops = new List<Operator>
        {
            Op("炎1", ["炎"], ["突袭"]), Op("炎2", ["炎"], ["突袭"]), Op("炎3", ["炎"], ["突袭"]), Op("炎4", ["炎"], ["突袭"]),
            Healer(Op("医疗", ["维多利亚"]))
        };
        var planner = new LineupPlanner(ops, [Core("炎"), Core("维多利亚"), Extra("突袭")]);
        var lineups = planner.Solve(new PlannerRequest
        {
            Goals = [new("炎", 1, 3, false, true)], PeopleOptions = [4], TransformOptions = [0]
        });

        lineups.Should().NotBeEmpty();
        lineups.Should().OnlyContain(l => l.Operators.Any(o => o.Name == "医疗") && !l.MissingHealer);
    }

    [Fact]
    public void Plan_FlagsLineupsWhenTargetsLeaveNoRoomForAHealer()
    {
        var ops = new List<Operator>
        {
            Op("炎1", ["炎"]), Op("炎2", ["炎"]), Op("炎3", ["炎"]), Healer(Op("医疗", ["维多利亚"]))
        };
        var planner = new LineupPlanner(ops, [Core("炎"), Core("维多利亚")]);
        var lineups = planner.Solve(new PlannerRequest
        {
            Goals = [new("炎", 1, 3, false, true)], PeopleOptions = [3], TransformOptions = [0]
        });

        lineups.Should().ContainSingle().Which.MissingHealer.Should().BeTrue();
    }

    [Fact]
    public void Solve_ReturnsEightAndNinePersonVariants()
    {
        var operators = Enumerable.Range(1, 9).Select(i => Op($"干员{i}", ["炎"]));
        var planner = new LineupPlanner(operators, [Core("炎")]);

        var results = planner.Solve(new PlannerRequest
        {
            Goals = [new PlannerGoal("炎", 1, 3, false, true)],
            TransformAllowed = new HashSet<string> { "炎" }
        });

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => (r.People == 8 || r.People == 9) && r.Operators.Count + r.FreeSlots == r.People);
    }

    [Fact]
    public void Solve_FillsTheRequestedPeopleCount()
    {
        var operators = Enumerable.Range(1, 9).Select(i => Op($"干员{i}", ["炎"]));
        var planner = new LineupPlanner(operators, [Core("炎")]);

        var results = planner.Solve(new PlannerRequest { Goals = [new PlannerGoal("炎", 1, 3, false, true)] });

        results.Should().OnlyContain(r => r.Operators.Count == r.People);
        results.Where(r => r.People == 8).Should().OnlyContain(r => r.FreeSlots == 0);
    }

    [Fact]
    public void Solve_RanksCompleteLineupsByAdditionalCovenantScore()
    {
        var operators = new List<Operator>
        {
            Op("炎甲", ["炎"]), Op("炎乙", ["炎"]), Op("炎丙", ["炎"]),
            Op("精准甲", extra: ["精准"]), Op("精准乙", extra: ["精准"]),
            Op("空甲"), Op("空乙"), Op("空丙"), Op("空丁"), Op("空戊")
        };
        var planner = new LineupPlanner(operators, [Core("炎"), Extra("精准")]);

        var result = planner.Plan(new PlannerRequest
        {
            Goals = [new PlannerGoal("炎", 1, 3, false, true)],
            PeopleOptions = [8], TransformOptions = [0]
        }, maxResultsPerVariant: 3);

        var best = result.AllLineups.First();
        best.Operators.Should().HaveCount(8);
        best.AdditionalScore.Should().Be(1);
        best.AdditionalCovenants.Should().ContainKey("精准");
    }

    [Fact]
    public void Solve_ManualSecondLevelCountsOnlyLevelOneScore()
    {
        var operators = new[]
        {
            Op("炎甲", ["炎"]), Op("炎乙", ["炎"]), Op("炎丙", ["炎"]),
            Op("突袭甲", extra: ["突袭"]), Op("突袭乙", extra: ["突袭"]), Op("突袭丙", extra: ["突袭"]),
            Op("空甲"), Op("空乙")
        };
        var planner = new LineupPlanner(operators, [Core("炎"), Extra("突袭")]);

        var result = planner.Plan(new PlannerRequest
        {
            Goals = [new PlannerGoal("炎", 1, 3, false, true)],
            PeopleOptions = [8], TransformOptions = [0]
        });

        result.AllLineups.Should().ContainSingle().Which.AdditionalScore.Should().Be(1);
    }

    [Fact]
    public void Solve_BannedAdditionalCovenantStillContributesToScore()
    {
        var operators = new[]
        {
            Op("炎甲", ["炎"]), Op("炎乙", ["炎"]), Op("炎丙", ["炎"]),
            Op("坚守甲", ["炎"], ["坚守"]), Op("坚守乙", ["炎"], ["坚守"]), Op("坚守丙", ["炎"], ["坚守"]),
            Op("空甲"), Op("空乙")
        };
        var planner = new LineupPlanner(operators, [Core("炎"), Extra("坚守")]);

        var result = planner.Plan(new PlannerRequest
        {
            Goals = [new PlannerGoal("炎", 1, 3, false, true)],
            BannedCovenants = new HashSet<string> { "坚守" },
            PeopleOptions = [8], TransformOptions = [0]
        });

        result.AllLineups.Should().ContainSingle().Which.AdditionalScore.Should().Be(3);
    }

    [Fact]
    public void Solve_PreparationAreaCovenantsDoNotContributeToAdditionalScore()
    {
        var operators = new[]
        {
            Op("炎甲", ["炎"]), Op("炎乙", ["炎"]), Op("炎丙", ["炎"]),
            Op("奇迹甲", extra: ["奇迹"]), Op("奇迹乙", extra: ["奇迹"]),
            Op("投资人甲", extra: ["投资人"]), Op("投资人乙", extra: ["投资人"]),
            Op("远见甲", extra: ["远见"]),
            Op("空甲"), Op("空乙")
        };
        var planner = new LineupPlanner(operators, [Core("炎"), Extra("奇迹"), Extra("投资人"), Extra("远见")]);

        var result = planner.Plan(new PlannerRequest
        {
            Goals = [new PlannerGoal("炎", 1, 3, false, true)],
            PeopleOptions = [8], TransformOptions = [0]
        });

        result.AllLineups.First().AdditionalScore.Should().Be(0);
    }

    [Fact]
    public void Solve_DoesNotRepeatTheSameLineupAcrossPeopleCounts()
    {
        var operators = Enumerable.Range(1, 4).Select(i => Op($"干员{i}", ["炎"]));
        var planner = new LineupPlanner(operators, [Core("炎")]);

        var results = planner.Solve(new PlannerRequest { Goals = [new PlannerGoal("炎", 1, 3, false, true)], TransformOptions = [0] });

        var keys = results.Select(r => string.Join(",", r.Operators.Select(o => o.Name).Order())).ToList();
        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Solve_RecordsRequestedTransformation()
    {
        var operators = Enumerable.Range(1, 8).Select(i => Op($"干员{i}"));
        var planner = new LineupPlanner(operators, [Extra("助力")]);

        var results = planner.Solve(new PlannerRequest
        {
            Goals = [new PlannerGoal("助力", 1, 2, false, false)],
            TransformAllowed = new HashSet<string> { "助力" }
        });

        results.Should().Contain(r => r.Transforms > 0 && r.Operators.Any(o => o.TransformedCovenant == "助力"));
    }

    [Fact]
    public void Solve_NeverTransformsIntoNoTransformCovenants()
    {
        var operators = new[] { Op("甲"), Op("乙"), Op("丙", extra: ["灵巧"]) };
        var covenants = new[] { Extra("灵巧") };
        var planner = new LineupPlanner(operators, covenants);

        var results = planner.Solve(new PlannerRequest
        {
            Goals = [new PlannerGoal("灵巧", 1, 2, false, false)],
            TransformAllowed = new HashSet<string> { "灵巧" }
        });

        results.Should().BeEmpty();
        planner.DefaultTransformAllowed().Should().NotContain("灵巧");
    }

    [Fact]
    public void Solve_SoloCovenantRequiresExactlyOne()
    {
        var operators = new[] { Op("甲", extra: ["独行"]), Op("乙", extra: ["独行"]), Op("丙", ["炎"]), Op("丁", ["炎"]), Op("戊", ["炎"]) };
        var planner = new LineupPlanner(operators, [Core("炎"), Extra("独行", 1)]);

        var results = planner.Solve(new PlannerRequest
        {
            Goals = [new PlannerGoal("炎", 1, 3, false, true), new PlannerGoal("独行", 1, 1, false, false)],
            TransformOptions = [0]
        });

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => r.Counts["独行"] == 1);
    }

    [Fact]
    public void Solve_HarmonyOnlyBoostsCoreCovenantsAlreadyPresent()
    {
        var operators = new[] { Op("魔王", ["调和"]), Op("甲", ["炎"]), Op("乙", ["炎"]), Op("丙", ["乌萨斯"]) };
        var planner = new LineupPlanner(operators, [Core("炎"), Core("乌萨斯"), Extra("调和", 1)]);

        var withFire = planner.Solve(new PlannerRequest { Goals = [new PlannerGoal("炎", 1, 3, false, true)], TransformOptions = [0] });
        withFire.Should().Contain(r => r.Operators.Any(o => o.Name == "魔王") && r.HarmonyBonus["炎"] == 1);

        var operatorsWithoutFire = new[] { Op("魔王", ["调和"]), Op("丙", ["乌萨斯"]) };
        var harmonyOnly = new LineupPlanner(operatorsWithoutFire, [Core("炎"), Extra("调和", 1)])
            .Solve(new PlannerRequest { Goals = [new PlannerGoal("炎", 1, 1, false, true)], TransformOptions = [0] });
        harmonyOnly.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ExcludesOperatorsWhoseCovenantsAreBanned()
    {
        var operators = new[] { Op("单禁", ["炎"]), Op("双禁", ["炎"], ["突袭"]), Op("可用", ["炎"], ["精准"]), Op("甲", ["炎"]), Op("乙", ["维多利亚"], ["突袭"]) };
        var planner = new LineupPlanner(operators, [Core("炎"), Core("维多利亚"), Extra("突袭"), Extra("精准")]);
        var banned = new HashSet<string> { "炎", "突袭" };

        planner.IsExcludedByBans(operators[0], banned).Should().BeTrue();
        planner.IsExcludedByBans(operators[1], banned).Should().BeTrue();
        planner.IsExcludedByBans(operators[2], banned).Should().BeFalse();
        planner.IsExcludedByBans(operators[4], banned).Should().BeFalse();
    }

    [Fact]
    public void CovenantBans_RemoveOperatorsWhoseCovenantsAreAllBanned()
    {
        // 霍尔海雅: 奥术 + 精准, no core covenant.
        var hoederer = new Operator { Name = "霍尔海雅", AdditionalCovenants = ["奥术", "精准"] };
        CovenantBanRules.IsExcluded(hoederer, new HashSet<string> { "奥术" }).Should().BeFalse();
        CovenantBanRules.IsExcluded(hoederer, new HashSet<string> { "奥术", "精准" }).Should().BeTrue();
        // Two core covenants (哈洛德): both banned removes him, one does not.
        var harold = new Operator { Name = "哈洛德", CoreCovenants = ["维多利亚", "谢拉格"] };
        CovenantBanRules.IsExcluded(harold, new HashSet<string> { "维多利亚" }).Should().BeFalse();
        CovenantBanRules.IsExcluded(harold, new HashSet<string> { "维多利亚", "谢拉格" }).Should().BeTrue();
        // Three covenants: one left unbanned keeps the operator.
        var eyja = new Operator { Name = "纯烬艾雅法拉", CoreCovenants = ["莱塔尼亚"], AdditionalCovenants = ["聚合", "远见"] };
        CovenantBanRules.IsExcluded(eyja, new HashSet<string> { "莱塔尼亚", "聚合" }).Should().BeFalse();
        CovenantBanRules.IsExcluded(eyja, new HashSet<string> { "莱塔尼亚", "聚合", "远见" }).Should().BeTrue();
    }

    [Fact]
    public void Plan_BannedAdditionalCovenantCanStillBeTargeted()
    {
        // 突袭 is banned: 纯突袭 (only covenant banned) leaves the pool, but operators who also have a core covenant stay.
        var operators = new[] { Op("纯突袭", extra: ["突袭"]), Op("甲", ["炎"], ["突袭"]), Op("乙", ["维多利亚"], ["突袭"]), Op("丙", ["炎"]) };
        var planner = new LineupPlanner(operators, [Core("炎"), Core("维多利亚"), Extra("突袭")]);

        var result = planner.Plan(new PlannerRequest
        {
            Goals = [new PlannerGoal("突袭", 1, 2, false, false)],
            BannedCovenants = new HashSet<string> { "突袭" },
            TransformOptions = [0]
        });

        result.Error.Should().BeNull();
        result.AllLineups.Should().NotBeEmpty();
        result.AllLineups.Should().OnlyContain(l => l.Operators.All(o => o.Name != "纯突袭"));
    }

    [Fact]
    public void Plan_ReportsBannedCoreGoalAndUnavailableLockedOperator()
    {
        var planner = new LineupPlanner([Op("甲", ["炎"])], [Core("炎")]);

        planner.Plan(new PlannerRequest { Goals = [new PlannerGoal("炎", 1, 3, false, true)], BannedCovenants = new HashSet<string> { "炎" } })
            .Error.Should().Contain("炎");
        planner.Plan(new PlannerRequest { Goals = [new PlannerGoal("炎", 1, 1, false, true)], LockedOperators = new HashSet<string> { "甲" }, BannedOperators = new HashSet<string> { "甲" } })
            .Error.Should().Contain("甲");
    }

    [Fact]
    public void Plan_KeepsLockedOperatorsFirstAndMarked()
    {
        var operators = Enumerable.Range(1, 6).Select(i => Op($"干员{i}", ["炎"])).Append(Op("锁定", ["维多利亚"]));
        var planner = new LineupPlanner(operators, [Core("炎"), Core("维多利亚")]);

        var results = planner.Solve(new PlannerRequest
        {
            Goals = [new PlannerGoal("炎", 1, 3, false, true)],
            LockedOperators = new HashSet<string> { "锁定" },
            TransformOptions = [0]
        });

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => r.Operators[0].Name == "锁定" && r.Operators[0].IsLocked);
        results.SelectMany(r => r.Operators).Should().OnlyContain(o => o.IconPath.StartsWith("icons/operators/"));
    }

    [Fact]
    public void Plan_HonoursCancellation()
    {
        var operators = Enumerable.Range(1, 60).Select(i => Op($"干员{i}", [i % 2 == 0 ? "炎" : "维多利亚"]));
        var planner = new LineupPlanner(operators, [Core("炎"), Core("维多利亚")]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => planner.Plan(new PlannerRequest
        {
            Goals = [new PlannerGoal("炎", 2, 6, false, true), new PlannerGoal("维多利亚", 2, 6, false, true)]
        }, cts.Token, maxNodes: 1_000_000);

        act.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("乌萨斯", "SEASON", 2, 6, false)]
    [InlineData("不屈", "REGULAR", 2, 3, false)]
    [InlineData("突袭", "REGULAR", 2, 2, false)]
    [InlineData("绝技", "REGULAR", 2, 2, false)]
    [InlineData("独行", "REGULAR", 2, 1, false)]
    public void CreateGoal_MapsSecondLevelRules(string name, string bondType, int level, int expectedCount, bool manual)
    {
        var info = new CovenantInfo { Name = name, BondType = bondType, ActivateCount = name == "独行" ? 1 : bondType == "SEASON" ? 3 : 2 };

        var goal = LineupPlanner.CreateGoal(info, level);

        goal.Count.Should().Be(expectedCount);
        goal.Manual.Should().Be(manual);
    }
}
