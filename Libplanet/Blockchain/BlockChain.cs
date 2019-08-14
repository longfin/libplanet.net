using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using Libplanet.Action;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.Crypto;
using Libplanet.Store;
using Libplanet.Tx;
using Serilog;

[assembly: InternalsVisibleTo("Libplanet.Tests")]
namespace Libplanet.Blockchain
{
    public class BlockChain<T> : IReadOnlyList<Block<T>>
        where T : IAction, new()
    {
        // FIXME: The _rwlock field should be private.
        [SuppressMessage(
            "StyleCop.CSharp.OrderingRules",
            "SA1401:FieldsMustBePrivate",
            Justification = "Temporary visibility.")]
        internal readonly ReaderWriterLockSlim _rwlock;
        private readonly object _txLock;

        public BlockChain(IBlockPolicy<T> policy, IStore store)
            : this(
                policy,
                store,
                store.GetCanonicalNamespace()
            )
        {
        }

        internal BlockChain(IBlockPolicy<T> policy, IStore store, string @namespace)
            : this(
                policy,
                store,
                @namespace is null ? Guid.NewGuid() : Guid.Parse(@namespace)
            )
        {
        }

        internal BlockChain(IBlockPolicy<T> policy, IStore store, Guid id)
        {
            Id = id;
            Policy = policy;
            Store = store;
            Blocks = new BlockSet<T>(store);
            Transactions = new TransactionSet<T>(store);

            _rwlock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            _txLock = new object();

            if (Store.GetCanonicalNamespace() is null)
            {
                Store.SetCanonicalNamespace(Id.ToString());
            }
        }

        ~BlockChain()
        {
            _rwlock?.Dispose();
        }

        public IBlockPolicy<T> Policy { get; }

        public Block<T> Tip
        {
            get
            {
                try
                {
                    return this[-1];
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }
        }

        public Guid Id { get; private set; }

        /// <summary>
        /// All <see cref="Block{T}"/>s in the <see cref="BlockChain{T}"/>
        /// storage, including orphan <see cref="Block{T}"/>s.
        /// Keys are <see cref="Block{T}.Hash"/>es and values are
        /// their corresponding <see cref="Block{T}"/>s.
        /// </summary>
        public IDictionary<HashDigest<SHA256>, Block<T>> Blocks
        {
            get; private set;
        }

        /// <summary>
        /// All <see cref="Transaction{T}"/>s in the <see cref="BlockChain{T}"/>
        /// storage, including orphan <see cref="Transaction{T}"/>s.
        /// Keys are <see cref="Transaction{T}.Id"/>s and values are
        /// their corresponding <see cref="Transaction{T}"/>s.
        /// </summary>
        public IDictionary<TxId, Transaction<T>> Transactions
        {
            get; private set;
        }

        /// <summary>
        /// All <see cref="Block{T}.Hash"/>es in the current index.  The genesis block's hash goes
        /// first, and the tip goes last.
        /// </summary>
        public IEnumerable<HashDigest<SHA256>> BlockHashes
        {
            get
            {
                try
                {
                    _rwlock.EnterUpgradeableReadLock();

                    IEnumerable<HashDigest<SHA256>> indices = Store.IterateIndex(
                        Id.ToString()
                    );

                    // NOTE: The reason why this does not simply return indices, but iterates over
                    // indices and yields hashes step by step instead, is that we need to ensure
                    // the read lock held until the whole iteration completes.
                    foreach (HashDigest<SHA256> hash in indices)
                    {
                        yield return hash;
                    }
                }
                finally
                {
                    _rwlock.ExitUpgradeableReadLock();
                }
            }
        }

        /// <inheritdoc/>
        int IReadOnlyCollection<Block<T>>.Count =>
            checked((int)Store.CountIndex(Id.ToString()));

        internal IStore Store { get; }

        // It's virtually a constant in production, but used for unit tests.
        internal int FindNextHashesChunkSize { get; set; } = 500;

        /// <inheritdoc/>
        public Block<T> this[int index] => this[(long)index];

        public Block<T> this[long index]
        {
            get
            {
                try
                {
                    _rwlock.EnterReadLock();

                    HashDigest<SHA256>? blockHash = Store.IndexBlockHash(
                        Id.ToString(), index
                    );
                    if (blockHash == null)
                    {
                        throw new ArgumentOutOfRangeException();
                    }

                    return Blocks[blockHash.Value];
                }
                finally
                {
                    _rwlock.ExitReadLock();
                }
            }
        }

        public void Validate(
            IReadOnlyList<Block<T>> blocks,
            DateTimeOffset currentTime
        )
        {
            InvalidBlockException e =
                Policy.ValidateBlocks(blocks, currentTime);

            if (e != null)
            {
                throw e;
            }
        }

        public IEnumerator<Block<T>> GetEnumerator()
        {
            return BlockHashes.Select(hash => Blocks[hash]).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets the state of the given <paramref name="addresses"/> in the
        /// <see cref="BlockChain{T}"/> from <paramref name="offset"/>.
        /// </summary>
        /// <param name="addresses">The list of <see cref="Address"/>es to get
        /// their states.</param>
        /// <param name="offset">The <see cref="HashDigest{T}"/> of the block to
        /// start finding the state. It will be The tip of the
        /// <see cref="BlockChain{T}"/> if it is <c>null</c>.</param>
        /// <param name="completeStates">When the <see cref="BlockChain{T}"/>
        /// instance does not contain states dirty of the block which lastly
        /// updated states of a requested address, this option makes
        /// the incomplete states calculated and filled on the fly.
        /// If this option is turned off (which is default) this method throws
        /// <see cref="IncompleteBlockStatesException"/> instead
        /// for the same situation.
        /// Just-in-time calculation of states could take a long time so that
        /// the overall latency of an application may rise.</param>
        /// <returns>The <see cref="AddressStateMap"/> of given
        /// <paramref name="addresses"/>.</returns>
        /// <exception cref="IncompleteBlockStatesException">Thrown when
        /// the <see cref="BlockChain{T}"/> instance does not contain
        /// states dirty of the block which lastly updated states of a requested
        /// address, because actions in the block have never been executed.
        /// If <paramref name="completeStates"/> option is turned on
        /// this exception is not thrown and incomplete states are calculated
        /// and filled on the fly instead.
        /// </exception>
        public AddressStateMap GetStates(
            IEnumerable<Address> addresses,
            HashDigest<SHA256>? offset = null,
            bool completeStates = false
        )
        {
            _rwlock.EnterReadLock();
            try
            {
                if (offset == null)
                {
                    offset = Store.IndexBlockHash(Id.ToString(), -1);
                }
            }
            finally
            {
                _rwlock.ExitReadLock();
            }

            var states = new AddressStateMap();

            if (offset == null)
            {
                return states;
            }

            Block<T> block = Blocks[offset.Value];

            ImmutableHashSet<Address> requestedAddresses =
                addresses.ToImmutableHashSet();
            var stateReferences = new HashSet<Tuple<HashDigest<SHA256>, long>>();

            foreach (var address in requestedAddresses)
            {
                Tuple<HashDigest<SHA256>, long> sr;
                _rwlock.EnterReadLock();
                try
                {
                    sr = Store.LookupStateReference(Id.ToString(), address, block);
                }
                finally
                {
                    _rwlock.ExitReadLock();
                }

                if (!(sr is null))
                {
                    stateReferences.Add(sr);
                }
            }

            IEnumerable<HashDigest<SHA256>> hashValues = stateReferences
                .OrderByDescending(sr => sr.Item2)
                .Select(sr => sr.Item1);

            foreach (var hashValue in hashValues)
            {
                AddressStateMap blockStates = Store.GetBlockStates(hashValue);
                if (blockStates is null)
                {
                    if (completeStates)
                    {
                        // Calculates and fills the incomplete states
                        // on the fly.
                        foreach (Block<T> b in this)
                        {
                            if (!(Store.GetBlockStates(b.Hash) is null))
                            {
                                continue;
                            }

                            List<ActionEvaluation> evaluations =
                                b.Evaluate(
                                    DateTimeOffset.UtcNow,
                                    a => GetStates(
                                        new[] { a },
                                        b.PreviousHash
                                    ).GetValueOrDefault(a)
                                ).ToList();

                            if (Policy.BlockAction is IAction)
                            {
                                evaluations.Add(EvaluateBlockAction(b, evaluations));
                            }

                            _rwlock.EnterWriteLock();

                            try
                            {
                                SetStates(b, evaluations, buildStateReferences: false);
                            }
                            finally
                            {
                                _rwlock.ExitWriteLock();
                            }
                        }

                        blockStates = Store.GetBlockStates(hashValue);
                        if (blockStates is null)
                        {
                            throw new NullReferenceException();
                        }
                    }
                    else
                    {
                        throw new IncompleteBlockStatesException(hashValue);
                    }
                }

                states = (AddressStateMap)states.SetItems(
                    blockStates.Where(kv =>
                        requestedAddresses.Contains(kv.Key) &&
                        !states.ContainsKey(kv.Key)
                    )
                );
            }

            return states;
        }

        /// <summary>
        /// Adds a <paramref name="block"/> to the end of this chain.
        /// <para>Note that <see cref="IAction.Render"/> methods of
        /// all <see cref="IAction"/> objects that belong
        /// to the <paramref name="block"/> are called right after
        /// the <paramref name="block"/> is confirmed (and thus all states
        /// reflect changes in the <paramref name="block"/>).</para>
        /// </summary>
        /// <param name="block">A next <see cref="Block{T}"/>, which is mined,
        /// to add.</param>
        /// <exception cref="InvalidBlockException">Thrown when the given
        /// <paramref name="block"/> is invalid, in itself or according to
        /// the <see cref="Policy"/>.</exception>
        /// <exception cref="InvalidTxNonceException">Thrown when the
        /// <see cref="Transaction{T}.Nonce"/> is different from
        /// <see cref="GetNextTxNonce"/> result of the
        /// <see cref="Transaction{T}.Signer"/>.</exception>
        public void Append(Block<T> block) =>
            Append(block, DateTimeOffset.UtcNow);

        /// <summary>
        /// Adds a <paramref name="block"/> to the end of this chain.
        /// <para>Note that <see cref="IAction.Render"/> methods of
        /// all <see cref="IAction"/> objects that belong
        /// to the <paramref name="block"/> are called right after
        /// the <paramref name="block"/> is confirmed (and thus all states
        /// reflect changes in the <paramref name="block"/>).</para>
        /// </summary>
        /// <param name="block">A next <see cref="Block{T}"/>, which is mined,
        /// to add.</param>
        /// <param name="currentTime">The current time.</param>
        /// <exception cref="InvalidBlockException">Thrown when the given
        /// <paramref name="block"/> is invalid, in itself or according to
        /// the <see cref="Policy"/>.</exception>
        /// <exception cref="InvalidTxNonceException">Thrown when the
        /// <see cref="Transaction{T}.Nonce"/> is different from
        /// <see cref="GetNextTxNonce"/> result of the
        /// <see cref="Transaction{T}.Signer"/>.</exception>
        public void Append(Block<T> block, DateTimeOffset currentTime) =>
            Append(block, currentTime, evaluateActions: true, renderActions: true);

        /// <summary>
        /// Adds <paramref name="transactions"/> to the pending list so that
        /// a next <see cref="Block{T}"/> to be mined contains these
        /// <paramref name="transactions"/>.
        /// </summary>
        /// <param name="transactions"> <see cref="Transaction{T}"/>s to add to the pending list.
        /// Keys are <see cref="Transactions"/>s and values are whether to broadcast.</param>
        public void StageTransactions(IDictionary<Transaction<T>, bool> transactions)
        {
            _rwlock.EnterWriteLock();

            try
            {
                foreach (KeyValuePair<Transaction<T>, bool> kv in transactions)
                {
                    var tx = kv.Key;
                    Transactions[tx.Id] = tx;
                }

                Store.StageTransactionIds(
                    transactions.ToDictionary(kv => kv.Key.Id, kv => kv.Value));
            }
            finally
            {
                _rwlock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes <paramref name="transactions"/> from the pending list.
        /// </summary>
        /// <param name="transactions"><see cref="Transaction{T}"/>s
        /// to remove from the pending list.</param>
        /// <seealso cref="StageTransactions"/>
        public void UnstageTransactions(ISet<Transaction<T>> transactions)
        {
            _rwlock.EnterWriteLock();

            try
            {
                Store.UnstageTransactionIds(
                    transactions.Select(tx => tx.Id).ToImmutableHashSet());
            }
            finally
            {
                _rwlock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Gets next <see cref="Transaction{T}.Nonce"/> of the address.
        /// </summary>
        /// <param name="address">The <see cref="Address"/> from which to obtain the
        /// <see cref="Transaction{T}.Nonce"/> value.</param>
        /// <returns>The next <see cref="Transaction{T}.Nonce"/> value of the
        /// <paramref name="address"/>.</returns>
        public long GetNextTxNonce(Address address)
        {
            long nonce = Store.GetTxNonce(Id.ToString(), address);
            IEnumerable<Transaction<T>> stagedTxs = Store
                .IterateStagedTransactionIds()
                .Select(Store.GetTransaction<T>)
                .Where(tx => tx.Signer.Equals(address))
                .OrderBy(tx => tx.Nonce);

            long prevNonce = nonce - 1;
            foreach (long n in stagedTxs.Select(tx => tx.Nonce))
            {
                if (n != prevNonce + 1)
                {
                    return prevNonce + 1;
                }

                nonce = n + 1;
                prevNonce = n;
            }

            return nonce;
        }

        public Block<T> MineBlock(
            Address miner,
            DateTimeOffset currentTime
        )
        {
            string @namespace = Id.ToString();
            long index = Store.CountIndex(@namespace);
            long difficulty = Policy.GetNextBlockDifficulty(this);
            HashDigest<SHA256>? prevHash = Store.IndexBlockHash(
                @namespace,
                index - 1
            );
            IEnumerable<Transaction<T>> transactions = Store
                .IterateStagedTransactionIds()
                .Select(Store.GetTransaction<T>)
                .Where(tx => tx.Nonce < GetNextTxNonce(tx.Signer));

            Block<T> block = Block<T>.Mine(
                index: index,
                difficulty: difficulty,
                miner: miner,
                previousHash: prevHash,
                timestamp: currentTime,
                transactions: transactions
            );
            Append(block, currentTime);

            return block;
        }

        public Block<T> MineBlock(Address miner) =>
            MineBlock(miner, DateTimeOffset.UtcNow);

        /// <summary>
        /// Creates a new <see cref="Transaction{T}"/> and stage the transaction.
        /// </summary>
        /// <param name="privateKey">A <see cref="PrivateKey"/> of the account who creates and
        /// signs a new transaction.</param>
        /// <param name="actions">A list of <see cref="IAction"/>s to include to a new transaction.
        /// </param>
        /// <param name="updatedAddresses"><see cref="Address"/>es whose states affected by
        /// <paramref name="actions"/>.</param>
        /// <param name="timestamp">The time this <see cref="Transaction{T}"/> is created and
        /// signed.</param>
        /// <param name="broadcast">Whether to broadcast created transaction.</param>
        /// <returns>A created new <see cref="Transaction{T}"/> signed by the given
        /// <paramref name="privateKey"/>.</returns>
        /// <seealso cref="Transaction{T}.Create" />
        public Transaction<T> MakeTransaction(
            PrivateKey privateKey,
            IEnumerable<T> actions,
            IImmutableSet<Address> updatedAddresses = null,
            DateTimeOffset? timestamp = null,
            bool broadcast = true)
        {
            timestamp = timestamp ?? DateTimeOffset.UtcNow;
            lock (_txLock)
            {
                Transaction<T> tx = Transaction<T>.Create(
                    GetNextTxNonce(privateKey.PublicKey.ToAddress()),
                    privateKey,
                    actions,
                    updatedAddresses,
                    timestamp);
                StageTransactions(new Dictionary<Transaction<T>, bool> { { tx, broadcast } });

                return tx;
            }
        }

        internal void Append(
            Block<T> block,
            DateTimeOffset currentTime,
            bool evaluateActions,
            bool renderActions
        )
        {
            if (!evaluateActions && renderActions)
            {
                throw new ArgumentException(
                    $"{nameof(renderActions)} option requires {nameof(evaluateActions)} " +
                    "to be turned on.",
                    nameof(renderActions)
                );
            }

            _rwlock.EnterUpgradeableReadLock();
            try
            {
                InvalidBlockException e =
                    Policy.ValidateNextBlock(this, block);

                if (!(e is null))
                {
                    throw e;
                }

                string ns = Id.ToString();
                HashDigest<SHA256>? tip = Store.IndexBlockHash(ns, -1);

                var nonceDeltas = new Dictionary<Address, long>();
                foreach (Transaction<T> tx1 in block.Transactions)
                {
                    Address txSigner = tx1.Signer;
                    nonceDeltas.TryGetValue(txSigner, out var nonceDelta);

                    long expectedNonce = nonceDelta + Store.GetTxNonce(Id.ToString(), txSigner);

                    if (!expectedNonce.Equals(tx1.Nonce))
                    {
                        throw new InvalidTxNonceException(
                            tx1.Id,
                            expectedNonce,
                            tx1.Nonce,
                            "Transaction nonce is invalid."
                        );
                    }

                    nonceDeltas[txSigner] = nonceDelta + 1;
                }

                _rwlock.EnterWriteLock();
                try
                {
                    Blocks[block.Hash] = block;
                    foreach (KeyValuePair<Address, long> pair in nonceDeltas)
                    {
                        Store.IncreaseTxNonce(ns, pair.Key, pair.Value);
                    }

                    Store.AppendIndex(ns, block.Hash);
                    ISet<TxId> txIds = block.Transactions
                        .Select(t => t.Id)
                        .ToImmutableHashSet();

                    Store.UnstageTransactionIds(txIds);
                }
                finally
                {
                    _rwlock.ExitWriteLock();
                }
            }
            finally
            {
                _rwlock.ExitUpgradeableReadLock();
            }

            if (evaluateActions)
            {
                ExecuteActions(block, renderActions);
            }
        }

        /// <summary>
        /// Evaluates actions in the given <paramref name="block"/> and fills states with the
        /// results, and renders them if <paramref name="render"/> is turned on.
        /// </summary>
        /// <param name="block">A block to execute.</param>
        /// <param name="render">Whether to render actions.  This is not idempotent; even if
        /// the given <paramref name="block"/> has executed before in the blockchain,
        /// its actions are rendered anyway.</param>
        /// <remarks>This method is idempotent (except for rendering).  If the given
        /// <paramref name="block"/> has executed before, it does not execute it nor mutate states.
        /// </remarks>
        internal void ExecuteActions(Block<T> block, bool render)
        {
            IReadOnlyList<ActionEvaluation> EvaluateActions()
            {
                AccountStateGetter stateGetter;
                if (block.PreviousHash is null)
                {
                    stateGetter = _ => null;
                }
                else
                {
                    stateGetter = a =>
                        GetStates(new[] { a }, block.PreviousHash).GetValueOrDefault(a);
                }

                ImmutableList<ActionEvaluation> txEvaluations = block
                    .Evaluate(DateTimeOffset.UtcNow, stateGetter)
                    .ToImmutableList();
                return Policy.BlockAction is IAction
                    ? txEvaluations.Add(EvaluateBlockAction(block, txEvaluations))
                    : txEvaluations;
            }

            IReadOnlyList<ActionEvaluation> evaluations = null;
            if (Store.GetBlockStates(block.Hash) is null)
            {
                evaluations = EvaluateActions();
                _rwlock.EnterWriteLock();
                try
                {
                    SetStates(block, evaluations, buildStateReferences: true);
                }
                finally
                {
                    _rwlock.ExitWriteLock();
                }
            }

            if (render)
            {
                if (evaluations is null)
                {
                    evaluations = EvaluateActions();
                }

                foreach (var evaluation in evaluations)
                {
                    evaluation.Action.Render(
                        evaluation.InputContext,
                        evaluation.OutputStates
                    );
                }
            }
        }

        internal ActionEvaluation EvaluateBlockAction(
            Block<T> block,
            IReadOnlyList<ActionEvaluation> txActionEvaluations)
        {
            if (Policy.BlockAction is null)
            {
                var message = "To evaluate block action, Policy.BlockAction must not be null.";
                throw new InvalidOperationException(message);
            }

            IAccountStateDelta lastStates = null;

            if (!(txActionEvaluations is null) && txActionEvaluations.Count > 0)
            {
                lastStates = txActionEvaluations[txActionEvaluations.Count - 1].OutputStates;
            }

            Address miner = block.Miner.GetValueOrDefault();

            var minerState = GetStates(new[] { miner }, block.PreviousHash)
                .GetValueOrDefault(miner);

            if (lastStates is null)
            {
                lastStates = new AccountStateDeltaImpl(a => minerState);
            }
            else if (lastStates.GetState(miner) is null)
            {
                lastStates = lastStates.SetState(miner, minerState);
            }

            return ActionEvaluation.EvaluateActionsGradually(
                block.Hash,
                block.Index,
                lastStates,
                miner,
                miner,
                Array.Empty<byte>(),
                new[] { Policy.BlockAction }.ToImmutableList()).First();
        }

        internal HashDigest<SHA256> FindBranchPoint(BlockLocator locator)
        {
            try
            {
                _rwlock.EnterReadLock();

                foreach (HashDigest<SHA256> hash in locator)
                {
                    if (Blocks.ContainsKey(hash))
                    {
                        return hash;
                    }
                }

                return this[0].Hash;
            }
            finally
            {
                _rwlock.ExitReadLock();
            }
        }

        internal IEnumerable<HashDigest<SHA256>> FindNextHashes(
            BlockLocator locator,
            HashDigest<SHA256>? stop = null)
        {
            int count = FindNextHashesChunkSize;
            try
            {
                _rwlock.EnterReadLock();

                HashDigest<SHA256>? tip = Store.IndexBlockHash(
                    Id.ToString(), -1);
                if (tip is null)
                {
                    yield break;
                }

                HashDigest<SHA256> branchPoint = FindBranchPoint(locator);
                var branchPointIndex = (int)Blocks[branchPoint].Index;
                IEnumerable<HashDigest<SHA256>> hashes = Store
                    .IterateIndex(Id.ToString(), branchPointIndex, count);

                foreach (HashDigest<SHA256> hash in hashes)
                {
                    if (count == 0)
                    {
                        yield break;
                    }

                    yield return hash;

                    if (hash.Equals(stop))
                    {
                        yield break;
                    }

                    count--;
                }
            }
            finally
            {
                _rwlock.ExitReadLock();
            }
        }

        internal BlockChain<T> Fork(HashDigest<SHA256> point)
        {
            Log.Debug("Fork debug mode.");
            var sw = new Stopwatch();
            sw.Start();
            var forked = new BlockChain<T>(Policy, Store, Guid.NewGuid());
            string id = Id.ToString();
            string forkedId = forked.Id.ToString();
            try
            {
                _rwlock.EnterReadLock();
                if (Store is LiteDBStore liteDBStore)
                {
                    liteDBStore.ForkIndex(id, forkedId, point);
                }
                else
                {
                    foreach (var index in Store.IterateIndex(id))
                    {
                        Store.AppendIndex(forkedId, index);
                        if (index.Equals(point))
                        {
                            break;
                        }
                    }
                }

                sw.Stop();
                Log.Debug($"index copy: {sw.Elapsed}");
                sw.Restart();

                Block<T> pointBlock = Blocks[point];

                var addressesToStrip = new HashSet<Address>();
                var signersToStrip = new Dictionary<Address, int>();

                for (
                    Block<T> block = Tip;
                    block.PreviousHash is HashDigest<SHA256> hash
                    && !block.Hash.Equals(point);
                    block = Blocks[hash])
                {
                    ImmutableHashSet<Address> addresses =
                        Store.GetBlockStates(block.Hash)
                        .Select(kv => kv.Key)
                        .ToImmutableHashSet();

                    addressesToStrip.UnionWith(addresses);

                    IEnumerable<(Address, int)> signers = block
                        .Transactions
                        .GroupBy(tx => tx.Signer)
                        .Select(g => (g.Key, g.Count()));

                    foreach ((Address address, int txCount) in signers)
                    {
                        int existingValue = 0;
                        signersToStrip.TryGetValue(address, out existingValue);
                        signersToStrip[address] = existingValue + txCount;
                    }
                }

                sw.Stop();
                Log.Debug($"calc addressstrips: {sw.Elapsed}");
                sw.Restart();

                Store.ForkStateReferences(
                    id,
                    forked.Id.ToString(),
                    pointBlock,
                    addressesToStrip.ToImmutableHashSet());

                sw.Stop();
                Log.Debug($"fork stateref: {sw.Elapsed}");
                sw.Restart();

                foreach (KeyValuePair<Address, long> pair in Store.ListTxNonces(id))
                {
                    Address address = pair.Key;
                    long existingNonce = pair.Value;
                    long txNonce = existingNonce;
                    int staleTxCount = 0;
                    if (signersToStrip.TryGetValue(address, out staleTxCount))
                    {
                        txNonce -= staleTxCount;
                    }

                    if (txNonce < 0)
                    {
                        throw new InvalidOperationException(
                            $"A tx nonce for {address} in the store seems broken.\n" +
                            $"Existing tx nonce: {existingNonce}\n" +
                            $"# of stale transactions: {staleTxCount}\n"
                        );
                    }

                    // Note that at this point every address has tx nonce = 0
                    // it's merely "setting" rather than "increasing."
                    Store.IncreaseTxNonce(forkedId, address, txNonce);
                }

                sw.Stop();
                Log.Debug($"tx proccess: {sw.Elapsed}");
                sw.Restart();
            }
            finally
            {
                _rwlock.ExitReadLock();
            }

            return forked;
        }

        internal BlockLocator GetBlockLocator(int threshold = 10)
        {
            try
            {
                _rwlock.EnterReadLock();

                HashDigest<SHA256>? current = Store.IndexBlockHash(
                    Id.ToString(), -1);
                long step = 1;
                var hashes = new List<HashDigest<SHA256>>();

                while (current is HashDigest<SHA256> hash)
                {
                    hashes.Add(hash);
                    Block<T> currentBlock = Blocks[hash];

                    if (currentBlock.Index == 0)
                    {
                        break;
                    }

                    long nextIndex = Math.Max(currentBlock.Index - step, 0);
                    current = Store.IndexBlockHash(Id.ToString(), nextIndex);

                    if (hashes.Count > threshold)
                    {
                        step *= 2;
                    }
                }

                return new BlockLocator(hashes);
            }
            finally
            {
                _rwlock.ExitReadLock();
            }
        }

        // FIXME it's very dangerous because replacing Id means
        // ALL blocks (referenced by MineBlock(), etc.) will be changed.
        // we need to add a synchronization mechanism to handle this correctly.
        internal void Swap(BlockChain<T> other, bool render)
        {
            // Finds the branch point.
            Block<T> topmostCommon = null;
            if (render && !(Tip is null || other.Tip is null))
            {
                long shorterHeight =
                    Math.Min(this.LongCount(), other.LongCount()) - 1;
                for (
                    Block<T> t = this[shorterHeight], o = other[shorterHeight];
                    t.PreviousHash is HashDigest<SHA256> tp &&
                        o.PreviousHash is HashDigest<SHA256> op;
                    t = Blocks[tp], o = other.Blocks[op]
                )
                {
                    if (t.Equals(o))
                    {
                        topmostCommon = t;
                        break;
                    }
                }
            }

            if (render)
            {
                // Unrender stale actions.
                for (
                    Block<T> b = Tip;
                    !(b is null) && b.Index > (topmostCommon?.Index ?? -1) &&
                        b.PreviousHash is HashDigest<SHA256> ph;
                    b = Blocks[ph]
                )
                {
                    List<ActionEvaluation> evaluations = b.EvaluateActionsPerTx(a =>
                            GetStates(new[] { a }, b.PreviousHash).GetValueOrDefault(a))
                        .Select(te => te.Item2).ToList();

                    if (Policy.BlockAction is IAction)
                    {
                        evaluations.Add(EvaluateBlockAction(b, evaluations));
                    }

                    evaluations.Reverse();

                    foreach (var evaluation in evaluations)
                    {
                        evaluation.Action.Unrender(
                            evaluation.InputContext,
                            evaluation.OutputStates
                        );
                    }
                }
            }

            try
            {
                _rwlock.EnterWriteLock();

                Guid obsoleteId = Id;
                Id = other.Id;
                Store.SetCanonicalNamespace(Id.ToString());
                Blocks = new BlockSet<T>(Store);
                Transactions = new TransactionSet<T>(Store);
                Store.DeleteNamespace(obsoleteId.ToString());
            }
            finally
            {
                _rwlock.ExitWriteLock();
            }

            if (render)
            {
                // Render actions that had been behind.
                IEnumerable<Block<T>> blocksToRender =
                    topmostCommon is Block<T> branchPoint
                        ? this.SkipWhile(b => b.Index <= branchPoint.Index)
                        : this;
                foreach (Block<T> b in blocksToRender)
                {
                    List<ActionEvaluation> evaluations = b.EvaluateActionsPerTx(a =>
                            GetStates(new[] { a }, b.PreviousHash).GetValueOrDefault(a))
                        .Select(te => te.Item2).ToList();

                    if (Policy.BlockAction is IAction)
                    {
                        evaluations.Add(EvaluateBlockAction(b, evaluations));
                    }

                    foreach (var evaluation in evaluations)
                    {
                        evaluation.Action.Render(
                            evaluation.InputContext,
                            evaluation.OutputStates
                        );
                    }
                }
            }
        }

        internal IEnumerable<TxId> GetStagedTransactionIds(bool toBroadcast)
        {
            _rwlock.EnterReadLock();

            try
            {
                return Store.IterateStagedTransactionIds(toBroadcast);
            }
            finally
            {
                _rwlock.ExitReadLock();
            }
        }

        internal void SetStates(
            Block<T> block,
            IReadOnlyList<ActionEvaluation> actionEvaluations,
            bool buildStateReferences
        )
        {
            HashDigest<SHA256> blockHash = block.Hash;
            IAccountStateDelta lastStates = actionEvaluations.Count > 0
                ? actionEvaluations[actionEvaluations.Count - 1].OutputStates
                : null;
            ImmutableHashSet<Address> updatedAddresses =
                actionEvaluations.Select(
                    a => a.OutputStates.UpdatedAddresses
                ).Aggregate(
                    ImmutableHashSet<Address>.Empty,
                    (a, b) => a.Union(b)
                );
            ImmutableDictionary<Address, object> totalDelta =
                updatedAddresses.Select(
                    a => new KeyValuePair<Address, object>(
                        a,
                        lastStates?.GetState(a)
                    )
                ).ToImmutableDictionary();

            Store.SetBlockStates(
                blockHash,
                new AddressStateMap(totalDelta)
            );

            if (buildStateReferences)
            {
                Store.StoreStateReference(Id.ToString(), updatedAddresses, block);
            }
        }
    }
}
