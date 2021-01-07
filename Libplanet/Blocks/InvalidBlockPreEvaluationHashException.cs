using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace Libplanet.Blocks
{
    [Serializable]
    [Equals]
    public class InvalidBlockPreEvaluationHashException : InvalidBlockException
    {
        /// <summary>
        /// Initializes a new instance of <see cref="InvalidBlockPreEvaluationHashException"/>
        /// class.
        /// </summary>
        /// <param name="preEvaluationHash">The hash digest of
        /// <see cref="Block{T}.PreEvaluationHash"/>.</param>
        /// <param name="calculatedPreEvalHash">The calculated hash digest from
        /// <see cref="Block{T}.Header"/>.</param>
        /// <param name="message">The message that describes the error.</param>
        public InvalidBlockPreEvaluationHashException(
            string message,
            HashDigest<SHA256> preEvaluationHash,
            HashDigest<SHA256> calculatedPreEvalHash)
            : base($"{message}\n" +
                $"In block header: {preEvaluationHash}\n" +
                $"Calculated: {calculatedPreEvalHash}")
        {
            PreEvaluationHash = preEvaluationHash;
            CalculatedHash = calculatedPreEvalHash;
        }

        protected InvalidBlockPreEvaluationHashException(
            SerializationInfo info,
            StreamingContext context)
            : base(info, context)
        {
            if ((byte[])info.GetValue(nameof(PreEvaluationHash), typeof(byte[])) is { } bHashBytes)
            {
                PreEvaluationHash = new HashDigest<SHA256>(bHashBytes);
            }

            if ((byte[])info.GetValue(nameof(CalculatedHash), typeof(byte[])) is { } cHashBytes)
            {
                CalculatedHash = new HashDigest<SHA256>(cHashBytes);
            }
        }

        /// <summary>
        /// The hash digest from actual block.
        /// </summary>
        public HashDigest<SHA256> PreEvaluationHash { get; private set; }

        /// <summary>
        /// The calculated hash digest from transactions in the block.
        /// </summary>
        public HashDigest<SHA256> CalculatedHash { get; private set; }

        public static bool operator ==(
            InvalidBlockPreEvaluationHashException left,
            InvalidBlockPreEvaluationHashException right
        ) => Operator.Weave(left, right);

        public static bool operator !=(
            InvalidBlockPreEvaluationHashException left,
            InvalidBlockPreEvaluationHashException right
        ) => Operator.Weave(left, right);

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(PreEvaluationHash), PreEvaluationHash.ToByteArray());
            info.AddValue(nameof(CalculatedHash), CalculatedHash.ToByteArray());
        }
    }
}
