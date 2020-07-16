using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Net;
using Libplanet.Tests.Blockchain;
using Libplanet.Tests.Common.Action;
using Libplanet.Tests.Store;
using Nekoyume.Action;
using Nekoyume.BlockChain;
using Nekoyume.Model.State;
using Xunit;

namespace Libplanet.RocksDBStore.Tests
{
    public class RocksDBStoreTest : StoreTest, IDisposable
    {
        private readonly RocksDBStoreFixture _fx;

        public RocksDBStoreTest()
        {
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

        [Fact]
        public void TestX()
        {
            var ss = nameof(Swarm<PolymorphicAction<ActionBase>>.StartAsync);
            var z = ss.Length;
#pragma warning disable MEN002 // Line is too long
            var s = new RocksDBStore(@"C:\Users\X1E\Downloads\4a321a45b07750ca7fa88a0a4a0c817fa26c5f5e54ac2ab91675256e6abed21a-snapshot-3e73fe19496ba143ca28949a7241e5bede2d375506b247169b5bf7f762000000");

            var policy = BlockPolicy.GetPolicy(10000);
            var genesis = s.GetBlock<PolymorphicAction<ActionBase>>(
                new HashDigest<SHA256>(ByteUtil.ParseHex("4a321a45b07750ca7fa88a0a4a0c817fa26c5f5e54ac2ab91675256e6abed21a"))
            );
            var chain = new BlockChain<PolymorphicAction<ActionBase>>(policy, s, genesis, render: false);
            var rankState = chain.GetState(RankingState.Address);
            var shopState = chain.GetState(ShopState.Address);
            var configState = chain.GetState(GameConfigState.Address);
            var waState = chain.GetState(new Address("0000000000000000000000000000000000000011"));

            var genState = s.GetBlockStates(genesis.Hash);
            var rankFromGen = genState[RankingState.Address.ToHex()];
            var lastRef = s.LookupStateReference(chain.Id, RankingState.Address.ToHex(), chain.Tip);
            var refs = s.IterateStateReferences(chain.Id, RankingState.Address.ToHex(), chain.Tip.Index, chain.Genesis.Index, 100).ToList();

            var lastWaRef = s.LookupStateReference(chain.Id, "0000000000000000000000000000000000000011", chain.Tip);
            var waRefs = s.IterateStateReferences(chain.Id, "0000000000000000000000000000000000000011", chain.Tip.Index, 0, 100).ToList();

            var blk = s.GetBlock<PolymorphicAction<ActionBase>>(lastRef.Item1);
            var tx = blk.Transactions.First();
            var act = tx.Actions.First();
            act.Execute(new PrevContext
            {
                BlockIndex = blk.Index,
                Miner = blk.Miner.Value,
                PreviousStates = new PrevState(chain, chain.Tip.Hash),
                Random = new Random(0),
                Rehearsal = false,
                Signer = tx.Signer,
            });

            var avatarAddress = ((HackAndSlash)act.InnerAction).avatarAddress;
            var lastAvatarRef = s.LookupStateReference(chain.Id, avatarAddress.ToHex(), chain.Tip);
            var avatarRefs = s.IterateStateReferences(chain.Id, avatarAddress.ToHex(), chain.Tip.Index, 0, 1000).ToList();
            var lastAgentRef = s.LookupStateReference(chain.Id, tx.Signer.ToHex(), chain.Tip);
            var agentRefs = s.IterateStateReferences(chain.Id, tx.Signer.ToHex(), chain.Tip.Index, 0, 1000).ToList();
            var avatarState = chain.GetState(avatarAddress);

            var blk2 = chain[105157];
            var blk2State = s.GetBlockStates(blk2.Hash);
            var tx2 = blk2.Transactions.First(t => t.Signer == tx.Signer);
            var act2 = tx2.Actions.First();

            var chainIds = s.ListChainIds().ToList();

            var x = new Address("b227989ce51fc0ab07c2c5be05ba8062d1a42e8e");
            var lastXRef = s.LookupStateReference(chain.Id, x.ToHex(), chain.Tip);
            var xRefs = s.IterateStateReferences(chain.Id, x.ToHex(), chain.Tip.Index, 0, 1000);

            var cf = s.GetColumnFamily(s.StateRefDb, chain.Id);
            var stateRefDb = s.StateRefDb;
            var raw0 = stateRefDb.Get(s.StateRefKey(RankingState.Address.ToHex(), blk.Index), cf);
            var raw1 = stateRefDb.Get(s.StateRefKey(avatarAddress.ToHex().ToLower(), blk2.Index), cf);
            var raw2 = stateRefDb.Get(s.StateRefKey(GameConfigState.Address.ToHex(), genesis.Index), cf);

            var ks = new HashSet<string>();
            var prefix = new[] { (byte)'s' }.Concat(RocksDBStoreBitConverter.GetBytes(x.ToHex())).ToArray();
            using (var srIt = stateRefDb.NewIterator(cf))
            {
                for (srIt.SeekToFirst(); srIt.Valid(); srIt.Next())
                {
                    if (srIt.Key().StartsWith(new[] { (byte)'s' }))
                    {
                        ks.Add(Encoding.UTF8.GetString(srIt.Key().Skip(1).Take(40).ToArray()));
                    }
                }
            }

            var exists = ks.Contains(avatarAddress.ToHex());

            Assert.False(false);
#pragma warning restore MEN002 // Line is too long
        }

        private class PrevContext : IActionContext
        {
            public Address Signer { get; set; }

            public Address Miner { get; set; }

            public long BlockIndex { get; set; }

            public bool Rehearsal { get; set; }

            public IAccountStateDelta PreviousStates { get; set; }

            public IRandom Random { get; set; }
        }

        private class Random : System.Random, IRandom
        {
            public Random(int seed)
                : base(seed)
            {
            }
        }

        private class PrevState : IAccountStateDelta
        {
            private BlockChain<PolymorphicAction<ActionBase>> _chain;

            private HashDigest<SHA256> _baseHash;

            public PrevState(
                BlockChain<PolymorphicAction<ActionBase>> chain,
                HashDigest<SHA256> baseHash)
            {
                _chain = chain;
                _baseHash = baseHash;
            }

            public IImmutableSet<Address> UpdatedAddresses =>
                throw new NotSupportedException();

            public IImmutableSet<Address> StateUpdatedAddresses =>
                throw new NotSupportedException();

            public IImmutableDictionary<Address, IImmutableSet<Currency>> UpdatedFungibleAssets =>
                throw new NotSupportedException();

            public IAccountStateDelta BurnAsset(Address owner, Currency currency, BigInteger amount)
            {
                throw new NotSupportedException();
            }

            public BigInteger GetBalance(Address address, Currency currency)
            {
                return _chain.GetBalance(address, currency, _baseHash);
            }

            public IValue GetState(Address address)
            {
                return _chain.GetState(address, _baseHash);
            }

            public IAccountStateDelta MintAsset(
                Address recipient,
                Currency currency,
                BigInteger amount)
            {
                throw new NotSupportedException();
            }

            public IAccountStateDelta SetState(Address address, IValue state)
            {
                return this;
            }

            public IAccountStateDelta TransferAsset(
                Address sender,
                Address recipient,
                Currency currency,
                BigInteger amount,
                bool allowNegativeBalance = false)
            {
                throw new NotSupportedException();
            }
        }
    }
}
