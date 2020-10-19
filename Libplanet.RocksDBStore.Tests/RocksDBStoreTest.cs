using System;
using System.IO;
using System.Security.Cryptography;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Store;
using Libplanet.Tests.Blockchain;
using Libplanet.Tests.Common.Action;
using Libplanet.Tests.Store;
using Nekoyume.Action;
using Nekoyume.BlockChain;
using Serilog;
using Serilog.Core;
using Xunit;
using Xunit.Abstractions;

namespace Libplanet.RocksDBStore.Tests
{
    public class RocksDBStoreTest : StoreTest, IDisposable
    {
        private readonly RocksDBStoreFixture _fx;

        public RocksDBStoreTest(ITestOutputHelper output)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.WithThreadId()
                .WriteTo.TestOutput(output)
                .CreateLogger()
                .ForContext<RocksDBStoreTest>();
            try
            {
                Fx = _fx = new RocksDBStoreFixture();
                FxConstructor = () => new RocksDBStoreFixture();
            }
            catch (TypeInitializationException)
            {
                throw new SkipException("RocksDB is not available.");
            }
        }

        public void Dispose()
        {
            _fx?.Dispose();
        }

#pragma warning disable MEN002 // Line is too long
#pragma warning disable S125 // Sections of code should not be commented out
        [Fact]
        public void D()
        {
            var store = new RocksDBStore(@"C:\Users\X1E\AppData\Local\planetarium\9c-beta-8-rc4");
            var stateKV = new RocksDBKeyValueStore(@"C:\Users\X1E\AppData\Local\planetarium\9c-beta-8-rc4\states");
            var stateStore = new TrieStateStore(
                stateKV,
                new RocksDBKeyValueStore(@"C:\Users\X1E\AppData\Local\planetarium\9c-beta-8-rc4\state_hashes")
            );
            var genesis = store.GetBlock<PolymorphicAction<ActionBase>>(
                new HashDigest<SHA256>(ByteUtil.ParseHex("ed4f6ba2a3f6834b7701b5d483b1a3b9ea1338ddd0c22708eb85900c9e9ec1b5"))
            );
            var bc = new BlockChain<PolymorphicAction<ActionBase>>(
                new BlockPolicySource(Logger.None).GetPolicy(5000000),
                store,
                stateStore,
                genesis
            );

            var seedStore = new RocksDBStore(@"C:\Users\X1E\seed-data\seed");
            var seedStateKV = new RocksDBKeyValueStore(@"C:\Users\X1E\seed-data\seed\states");
            var seedStateStore = new TrieStateStore(
                seedStateKV,
                new RocksDBKeyValueStore(@"C:\Users\X1E\seed-data\seed\state_hashes")
            );

            for (var i = 100; i < 10000; i++)
            {
                var b = bc[i];
                Assert.Equal(
                    stateStore.GetRootHash(b.Hash), b.StateRootHash
                );
            }

            var seedBc = new BlockChain<PolymorphicAction<ActionBase>>(
                new BlockPolicySource(Logger.None).GetPolicy(5000000),
                seedStore,
                seedStateStore,
                genesis
            );
            var targetBc = bc.Fork(bc[14274].Hash);
            // var targetBc = seedBc.Fork(bc[14274].Hash);
            targetBc.Append(bc[14275]);
            /*
            for (var i = 14275; i <= b.Index; i++)
            {
                targetBc.Append(bc[i]);
            }
            */

            Assert.True(true);
        }
#pragma warning restore S125 // Sections of code should not be commented out
#pragma warning restore MEN002 // Line is too long

        [SkippableFact]
        public void ReopenStoreAfterDispose()
        {
            var path = Path.Combine(Path.GetTempPath(), $"rocksdb_test_{Guid.NewGuid()}");

            try
            {
                var store = new RocksDBStore(path);
                var blocks = new BlockChain<DumbAction>(
                    new NullPolicy<DumbAction>(),
                    store,
                    store,
                    Fx.GenesisBlock
                );
                store.Dispose();

                store = new RocksDBStore(path);
                store.Dispose();
            }
            finally
            {
                Directory.Delete(path, true);
            }
        }
    }
}
