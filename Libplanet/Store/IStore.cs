using System.Collections.Generic;
using Libplanet.Action;
using Libplanet.Blocks;
using Libplanet.Tx;

namespace Libplanet.Store
{
    public interface IStore
    {
        long CountIndex();

        IEnumerable<HashDigest> IterateIndex();

        HashDigest? IndexBlockHash(long index);

        long AppendIndex(HashDigest hash);

        IEnumerable<Address> IterateAddresses();

        IEnumerable<TxId> GetAddressTransactionIds(Address address);

        long AppendAddressTransactionId(Address address, TxId txId);

        void StageTransactionIds(ISet<TxId> txids);

        void UnstageTransactionIds(ISet<TxId> txids);

        IEnumerable<TxId> IterateStagedTransactionIds();

        IEnumerable<TxId> IterateTransactionIds();

        Transaction<T>? GetTransaction<T>(TxId txid)
            where T : IAction;

        void PutTransaction<T>(Transaction<T> tx)
            where T : IAction;

         bool DeleteTransaction(TxId txid);

        IEnumerable<HashDigest> IterateBlockHashes();

        Block<T>? GetBlock<T>(HashDigest blockHash)
            where T : IAction;

        void PutBlock<T>(Block<T> block)
            where T : IAction;

        bool DeleteBlock(HashDigest blockHash);

        States GetBlockStates(HashDigest blockHash);

        void SetBlockStates(HashDigest blockHash, States states);

        int CountTransactions();

        int CountBlocks();

        int CountAddresses();
    }
}
