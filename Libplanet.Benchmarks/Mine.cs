using BenchmarkDotNet.Attributes;
using Libplanet.Blocks;
using Libplanet.Tests.Common.Action;
using Libplanet.Tx;
using System;

namespace Libplanet.Benchmarks
{
    public class Mine
    {
        [Benchmark]
        public Block<DumbAction> MineBlockWithDifficulty()
        {
            return Block<DumbAction>.Mine(
                0,
                50000000,
                50000000,
                default(Address),
                null,
                DateTime.UtcNow,
                new Transaction<DumbAction>[] { }
            );
        }

    }
}
