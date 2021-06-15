#nullable enable
using System;
using System.Security.Cryptography;
using System.Threading;
using Libplanet.Blocks;

namespace Libplanet
{
    /// <summary>
    /// This contains a set of functions that implements
    /// <a href="https://en.wikipedia.org/wiki/Hashcash">Hashcash</a>,
    /// a <a href="https://en.wikipedia.org/wiki/Proof-of-work_system"
    /// >proof-of-work system</a>.
    /// </summary>
    public static class Hashcash
    {
        /// <summary>
        /// A delegate to determine a consistent <see cref="byte"/>s
        /// representation derived from a given <paramref name="nonce"/>.
        /// <para>Since it is called multiple times with different
        /// <paramref name="nonce"/>s for
        /// <a href="https://en.wikipedia.org/wiki/Proof-of-work_system"
        /// >proof-of-work system</a>, the total time an implementation elapses
        /// should not vary for different <paramref name="nonce"/>s.</para>
        /// </summary>
        /// <param name="nonce">An arbitrary nonce for an attempt, provided
        /// by <see cref="Hashcash.Answer(Stamp, long, CancellationToken)"/> method.</param>
        /// <returns>A <see cref="byte"/> array determined from the given
        /// <paramref name="nonce"/>.  It should return consistently
        /// an equivalent array for equivalent <paramref name="nonce"/>
        /// values.</returns>
        /// <seealso cref="Hashcash.Answer(Stamp, long, CancellationToken)"/>
        /// <seealso cref="Nonce"/>
        public delegate byte[] Stamp(byte[] nonce);

        /// <summary>
        /// Finds a <see cref="Nonce"/> that satisfies the given
        /// <paramref name="difficulty"/>.  This process is so-called
        /// &#x0201c;<a
        /// href="https://en.wikipedia.org/wiki/Cryptocurrency#Mining"
        /// >mining</a>&#x0201d;.
        /// </summary>
        /// <param name="stamp">A callback to get a &#x0201c;stamp&#x0201d;
        /// which is a <see cref="byte"/> array determined from a given
        /// <see cref="Nonce"/> value.</param>
        /// <param name="difficulty">A number to calculate the target number
        /// for which the returned answer should be less than.</param>
        /// <param name="cancellationToken">
        /// A cancellation token used to propagate notification that this
        /// operation should be canceled.
        /// </param>
        /// <returns>A <see cref="Nonce"/> value which satisfies the given
        /// <paramref name="difficulty"/>.</returns>
        /// <seealso cref="Stamp"/>
        public static Nonce Answer(
            Stamp stamp,
            long difficulty,
            CancellationToken cancellationToken = default(CancellationToken))
            => Answer(stamp, difficulty, 0, cancellationToken);

        public static Nonce Answer(
            Stamp stamp,
            long difficulty,
            int seed,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var nonceBytes = new byte[10];
            var random = new Random(seed);
            var c = 0;
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            while (!cancellationToken.IsCancellationRequested)
            {
                c += 1;
                random.NextBytes(nonceBytes);
                var digest = Hash(stamp(nonceBytes));

                if (digest.Satisfies(difficulty))
                {
                    sw.Stop();
                    return new Nonce(nonceBytes);
                }
            }

            throw new OperationCanceledException(cancellationToken);
        }

        /// <summary>
        /// Calculates a SHA-256 digest from the given <paramref name="bytes"/>.
        /// </summary>
        /// <param name="bytes">A <see cref="byte"/> array to calculate
        /// its hash digest.</param>
        /// <returns>A deterministic digest of the given
        /// <paramref name="bytes"/>.</returns>
        public static BlockHash Hash(byte[] bytes) =>
            BlockHash.FromHashDigest(HashDigest<SHA256>.DeriveFrom(bytes));
    }
}
