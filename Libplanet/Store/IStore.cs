using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Libplanet.Action;
using Libplanet.Blocks;
using Libplanet.Tx;

namespace Libplanet.Store
{
    public abstract class IStore
    {
        public abstract long CountIndex();

        public abstract IEnumerable<HashDigest> IterateIndex();

        public abstract HashDigest? IndexBlockHash(long index);

        public abstract long AppendIndex(HashDigest hash);

        public abstract IEnumerable<Address> IterateAddresses();

        public abstract IEnumerable<TxId> GetAddressTransactionIds(Address address);

        public abstract long AppendAddressTransactionId(Address address, TxId txId);

        public abstract void StageTransactionIds(ISet<TxId> txids);

        public abstract void UnstageTransactionIds(ISet<TxId> txids);

        public abstract IEnumerable<TxId> IterateStagedTransactionIds();

        public abstract IEnumerable<TxId> IterateTransactionIds();

        public abstract Transaction<T>? GetTransaction<T>(TxId txid)
            where T : IAction;

        public abstract void PutTransaction<T>(Transaction<T> tx)
            where T : IAction;

        public abstract bool DeleteTransaction(TxId txid);

        public abstract IEnumerable<HashDigest> IterateBlockHashes();

        public abstract Block<T>? GetBlock<T>(HashDigest blockHash)
            where T : IAction;

        public abstract void PutBlock<T>(Block<T> block)
            where T : IAction;

        public abstract bool DeleteBlock(HashDigest blockHash);

        // public abstract States GetBlockStates(HashDigest blockHash);
        // public abstract void SetBlockStates(HashDigest blockHash, States states);
        public int CountTransactions()
        {
            return IterateStagedTransactionIds().Count();
        }

        public int CountBlocks()
        {
            return IterateBlockHashes().Count();
        }

        public int CountAddresses()
        {
            return IterateAddresses().Count();
        }
    }
}
