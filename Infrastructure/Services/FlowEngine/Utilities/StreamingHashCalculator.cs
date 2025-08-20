using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Utilities
{
    /// <summary>
    /// Streaming hash calculator for large data payloads
    /// </summary>
    public static class StreamingHashCalculator
    {
        /// <summary>
        /// Compute hash without loading entire object into memory
        /// </summary>
        public static async Task<string> ComputeStreamingHashAsync(object data, CancellationToken cancellationToken = default)
        {
            using var hashAlgorithm = SHA256.Create();
            using var stream = new MemoryStream();

            // Use streaming JSON serialization
            await JsonSerializer.SerializeAsync(stream, data, JsonSerializerOptions.Default, cancellationToken).ConfigureAwait(false);

            stream.Position = 0;
            var hashBytes = await hashAlgorithm.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);

            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// Compute hash for signing payloads efficiently
        /// </summary>
        public static async Task<string> ComputeSigningHashAsync(string eventType, object eventData, string publishedBy, string correlationId, CancellationToken cancellationToken = default)
        {
            using var hashAlgorithm = SHA256.Create();

            // Build payload incrementally to avoid large string concatenation
            var components = new[]
            {
                Encoding.UTF8.GetBytes(eventType),
                Encoding.UTF8.GetBytes("|"),
                Encoding.UTF8.GetBytes(publishedBy),
                Encoding.UTF8.GetBytes("|"),
                Encoding.UTF8.GetBytes(correlationId),
                Encoding.UTF8.GetBytes("|")
            };

            // Hash components incrementally
            hashAlgorithm.TransformBlock(components[0], 0, components[0].Length, null, 0);
            hashAlgorithm.TransformBlock(components[1], 0, components[1].Length, null, 0);
            hashAlgorithm.TransformBlock(components[2], 0, components[2].Length, null, 0);
            hashAlgorithm.TransformBlock(components[3], 0, components[3].Length, null, 0);
            hashAlgorithm.TransformBlock(components[4], 0, components[4].Length, null, 0);
            hashAlgorithm.TransformBlock(components[5], 0, components[5].Length, null, 0);

            // Stream the event data
            using var dataStream = new MemoryStream();
            await JsonSerializer.SerializeAsync(dataStream, eventData, JsonSerializerOptions.Default, cancellationToken).ConfigureAwait(false);

            var dataBytes = dataStream.ToArray();
            hashAlgorithm.TransformFinalBlock(dataBytes, 0, dataBytes.Length);

            return Convert.ToHexString(hashAlgorithm.Hash ?? Array.Empty<byte>());
        }
    }
}
