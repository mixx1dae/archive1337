using SA.Packing;

namespace SA.Tests;

[TestClass]
public class KnapsackTests
{
    [TestMethod]
    public void Greedy_LosesToDp_OnClassicCase()
    {
        // Жадібний бере (6,7) за найкращим v/w і далі нічого не вміщує: 7. ДП бере два по 5: 10.
        var items = new[] { new KnapsackItem(6, 7), new KnapsackItem(5, 5), new KnapsackItem(5, 5) };

        var dp = Knapsack.SolveDp(items, 10);
        var greedy = Knapsack.SolveGreedy(items, 10);

        Assert.AreEqual(10L, Knapsack.TotalValue(items, dp));
        Assert.AreEqual(7L, Knapsack.TotalValue(items, greedy));
        CollectionAssert.AreEqual(new[] { 1, 2 }, dp);
    }

    [TestMethod]
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

            Assert.IsTrue(Knapsack.TotalWeight(items, chosen) <= capacity);
            Assert.AreEqual(BruteForce(items, capacity), Knapsack.TotalValue(items, chosen));
        }
    }

    [TestMethod]
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

            Assert.IsTrue(dp >= greedy);
        }
    }

    [TestMethod]
    public void EmptyItemsOrZeroCapacity_ReturnEmpty()
    {
        Assert.AreEqual(0, Knapsack.SolveDp(Array.Empty<KnapsackItem>(), 100).Length);
        Assert.AreEqual(0, Knapsack.SolveGreedy(Array.Empty<KnapsackItem>(), 100).Length);
        Assert.AreEqual(0, Knapsack.SolveDp(new[] { new KnapsackItem(5, 5) }, 0).Length);
    }

    [TestMethod]
    public void ItemHeavierThanCapacity_IsNeverChosen()
    {
        var items = new[] { new KnapsackItem(100, 1000), new KnapsackItem(3, 1) };
        CollectionAssert.AreEqual(new[] { 1 }, Knapsack.SolveDp(items, 10));
        CollectionAssert.AreEqual(new[] { 1 }, Knapsack.SolveGreedy(items, 10));
    }

    [TestMethod]
    public void InvalidArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Knapsack.SolveDp(new[] { new KnapsackItem(0, 1) }, 10));
        Assert.ThrowsExactly<ArgumentException>(() => Knapsack.SolveDp(new[] { new KnapsackItem(1, -1) }, 10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Knapsack.SolveDp(Array.Empty<KnapsackItem>(), -1));
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
