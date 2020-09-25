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
        [Fact]
        public void D()
        {
            var path = @"C:\Users\X1E\Downloads\9c-beta-7-rc9-2";
            var store = new RocksDBStore(path);
            var stateStore = new TrieStateStore(
                new RocksDBKeyValueStore(Path.Join(path, "states")),
                new RocksDBKeyValueStore(Path.Join(path, "state_hashes"))
            );
            var genesis = store.GetBlock<PolymorphicAction<ActionBase>>(
                new HashDigest<SHA256>(ByteUtil.ParseHex("147ffb3a120e82c5bd21e909b6dc872c9abe510c800f8f59880d538745a39d02"))
            );
            var bc = new BlockChain<PolymorphicAction<ActionBase>>(
                new BlockPolicySource(Logger.None).GetPolicy(5000000),
                store,
                stateStore,
                genesis
            );
            var newBc = new BlockChain<PolymorphicAction<ActionBase>>(
                new BlockPolicySource(Logger.None).GetPolicy(5000000),
                new RocksDBStore(Path.Join(path, "temp")),
                new TrieStateStore(
                    new RocksDBKeyValueStore(Path.Join(path, "temp", "states")),
                    new RocksDBKeyValueStore(Path.Join(path, "temp", "state_hashes"))
                ),
                genesis
            );
            var villain = bc[3895];
            newBc.Append(villain);

            Assert.True(true);
        }
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
