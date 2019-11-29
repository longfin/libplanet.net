using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using Libplanet.Action;
using Libplanet.Blocks;
using LiteDB;

namespace Libplanet.Store
{
    public static class StoreExtension
    {
        #pragma warning disable MEN002
        /// <summary>
        /// Looks up a state reference, which is a block's <see cref="Block{T}.Hash"/> that contains
        /// an action mutating the <paramref name="address"/>' state.
        /// </summary>
        /// <param name="store">The store object expected to contain the state reference.</param>
        /// <param name="chainId">The chain ID to look up a state reference.</param>
        /// <param name="address">The <see cref="Address"/> to look up.</param>
        /// <param name="lookupUntil">The upper bound (i.e., the latest block) of the search range.
        /// <see cref="Block{T}"/>s after <paramref name="lookupUntil"/> are ignored.</param>
        /// <returns>Returns a nullable tuple consisting of <see cref="Block{T}.Hash"/> and
        /// <see cref="Block{T}.Index"/> of the <see cref="Block{T}"/> with the state of the
        /// address.</returns>
        /// <typeparam name="T">An <see cref="IAction"/> class used with
        /// <paramref name="lookupUntil"/>.</typeparam>
        /// <seealso cref="IStore.StoreStateReference(Guid, IImmutableSet{Address}, HashDigest{SHA256}, long)"/>
        /// <seealso cref="IStore.IterateStateReferences(Guid, Address, long?, long?, int?)"/>
        #pragma warning restore MEN002
        public static Tuple<HashDigest<SHA256>, long> LookupStateReference<T>(
            this IStore store,
            Guid chainId,
            Address address,
            Block<T> lookupUntil)
            where T : IAction, new()
        {
            if (lookupUntil is null)
            {
                throw new ArgumentNullException(nameof(lookupUntil));
            }

            return store.IterateStateReferences(chainId, address, lookupUntil.Index, limit: 1)
                    .FirstOrDefault();
        }

        /// <summary>
        /// Lists all accounts, that have any states, in the given <paramref name="chainId"/> and
        /// their state references.
        /// </summary>
        /// <param name="store">A store object.</param>
        /// <param name="chainId">The chain ID to look up state references.</param>
        /// <param name="onlyAfter">Includes state references only made after the block
        /// this argument refers to, if present.</param>
        /// <param name="ignoreAfter">Excludes state references made after the block
        /// this argument refers to, if present.</param>
        /// <returns>A dictionary of account addresses to lists of their corresponding state
        /// references.  Each list of state references is in ascending order, i.e., the block
        /// closest to the genesis goes first and the block closest to the tip goes last.</returns>
        public static IImmutableDictionary<Address, IImmutableList<HashDigest<SHA256>>>
        ListAllStateReferences(
            this IStore store,
            Guid chainId,
            HashDigest<SHA256>? onlyAfter = null,
            HashDigest<SHA256>? ignoreAfter = null
        )
        {
            (HashDigest<SHA256>, long)? baseBlock =
                onlyAfter is HashDigest<SHA256> @base && store.GetBlockIndex(@base) is long baseIdx
                    ? (@base, baseIdx)
                    : null as (HashDigest<SHA256>, long)?;
            (HashDigest<SHA256>, long)? targetBlock =
                ignoreAfter is HashDigest<SHA256> tgt && store.GetBlockIndex(tgt) is long tgtIdx
                    ? (tgt, tgtIdx)
                    : null as (HashDigest<SHA256>, long)?;

            var highestIndex = targetBlock?.Item2 ?? long.MaxValue;
            var lowestIndex = baseBlock?.Item2 ?? -1;

            string collId = ((DefaultStore)store).StateRefId(chainId);
            LiteCollection<DefaultStore.StateRefDoc> coll = ((DefaultStore)store)._db
                .GetCollection<DefaultStore.StateRefDoc>(collId);

            IEnumerable<DefaultStore.StateRefDoc> stateRefs = coll.Find(
                Query.And(
                    Query.All("BlockIndex", Query.Ascending),
                    Query.Between("BlockIndex", lowestIndex + 1, highestIndex)
                )
            );

            var d = new Dictionary<Address, List<HashDigest<SHA256>>>();

            foreach (var stateRef in stateRefs)
            {
                var address = stateRef.Address;
                if (!d.ContainsKey(address))
                {
                    d[address] = new List<HashDigest<SHA256>>();
                }

                d[address].Add(stateRef.BlockHash);
            }

            var dd = new Dictionary<Address, IImmutableList<HashDigest<SHA256>>>();

            foreach (var kv in d)
            {
                dd[kv.Key] = kv.Value.ToImmutableList();
            }

            return dd.ToImmutableDictionary();
        }
    }
}
