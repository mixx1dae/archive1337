using Xunit;
using SA.Packing;

namespace SA.Tests;


public class KnapsackTests
{
    [Fact]
    public void Greedy_LosesToDp_OnClassicCase()
    {
        // Жадібний бере (6,7) за найкращим v/w і далі нічого не вміщує: 7. ДП бере два по 5: 10.
        var items = new[] { new KnapsackItem(6, 7), new KnapsackItem(5, 5), new KnapsackItem(5, 5) };

        var dp = Knapsack.SolveDp(items, 10);
        var greedy = Knapsack.SolveGreedy(items, 10);

        Assert.Equal(10L, Knapsack.TotalValue(items, dp));
        Assert.Equal(7L, Knapsack.TotalValue(items, greedy));
        Assert.Equal(new[] { 1, 2 }, dp);
    }

    [Fact]
    public void Dp_MatchesBruteForce_OnRandomSmallInstances()
    {
        var rng = new Random(12345);
        for (int round = 0; round < 200; round++)
        {
            int n = rng.Next(0, 11);
            int capacity = rng.Next(0, 40);
            var items = new KnapsackItem[n];
            for (int i = 0; i < n; i++)
                items[i] = new KnapsackItem(rng.Next(1, 20), rng.Next(0, 30));

            var chosen = Knapsack.SolveDp(items, capacity);

            Assert.True(Knapsack.TotalWeight(items, chosen) <= capacity);
            Assert.Equal(BruteForce(items, capacity), Knapsack.TotalValue(items, chosen));
        }
    }

    [Fact]
    public void Dp_IsNeverWorseThanGreedy()
    {
        var rng = new Random(777);
        for (int round = 0; round < 200; round++)
        {
            int n = rng.Next(1, 40);
            int capacity = rng.Next(1, 500);
            var items = new KnapsackItem[n];
            for (int i = 0; i < n; i++)
                items[i] = new KnapsackItem(rng.Next(15, 200), rng.Next(1, 100));

            long dp = Knapsack.TotalValue(items, Knapsack.SolveDp(items, capacity));
            long greedy = Knapsack.TotalValue(items, Knapsack.SolveGreedy(items, capacity));

            Assert.True(dp >= greedy);
        }
    }

    [Fact]
    public void EmptyItemsOrZeroCapacity_ReturnEmpty()
    {
        Assert.Equal(0, Knapsack.SolveDp(Array.Empty<KnapsackItem>(), 100).Length);
        Assert.Equal(0, Knapsack.SolveGreedy(Array.Empty<KnapsackItem>(), 100).Length);
        Assert.Equal(0, Knapsack.SolveDp(new[] { new KnapsackItem(5, 5) }, 0).Length);
    }

    [Fact]
    public void ItemHeavierThanCapacity_IsNeverChosen()
    {
        var items = new[] { new KnapsackItem(100, 1000), new KnapsackItem(3, 1) };
        Assert.Equal(new[] { 1 }, Knapsack.SolveDp(items, 10));
        Assert.Equal(new[] { 1 }, Knapsack.SolveGreedy(items, 10));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => Knapsack.SolveDp(new[] { new KnapsackItem(0, 1) }, 10));
        Assert.Throws<ArgumentException>(() => Knapsack.SolveDp(new[] { new KnapsackItem(1, -1) }, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => Knapsack.SolveDp(Array.Empty<KnapsackItem>(), -1));
    }

    [Fact]
    public void Sequential_TakesItemsInOrderWhileTheyFit_AndIsNeverBetterThanDp()
    {
        var items = new[] { new KnapsackItem(6, 1), new KnapsackItem(5, 9), new KnapsackItem(4, 9), new KnapsackItem(3, 1) };

        var seq = Knapsack.SolveSequential(items, 10);

        Assert.Equal(new[] { 0, 2 }, seq);          // 6, потім 5 не влазить, 4 влазить, 3 вже ні
        Assert.True(Knapsack.TotalWeight(items, seq) <= 10);
        Assert.True(Knapsack.TotalValue(items, Knapsack.SolveDp(items, 10)) >= Knapsack.TotalValue(items, seq));
        Assert.Equal(0, Knapsack.SolveSequential(Array.Empty<KnapsackItem>(), 10).Length);
    }

    private static long BruteForce(KnapsackItem[] items, int capacity)
    {
        long best = 0;
        for (int mask = 0; mask < (1 << items.Length); mask++)
        {
            long w = 0, v = 0;
            for (int i = 0; i < items.Length; i++)
                if ((mask & (1 << i)) != 0) { w += items[i].Weight; v += items[i].Value; }
            if (w <= capacity && v > best) best = v;
        }
        return best;
    }
}
