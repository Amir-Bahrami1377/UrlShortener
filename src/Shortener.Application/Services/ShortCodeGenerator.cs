using System.Security.Cryptography;
using Shortener.Application.Abstractions;
using Shortener.Application.Common;
using Shortener.Domain.Exceptions;

namespace Shortener.Application.Services;

public sealed record ShortCodeGeneratorSettings(int CodeLength, int MaxGenerationRetries);

public sealed class ShortCodeGenerator(ICodeReservationStore reservationStore, ShortCodeGeneratorSettings settings)
{
    // 0, O, 1, I, and lowercase l removed to avoid misreading (lowercase o and uppercase L stay).
    private const string Alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromSeconds(30);

    /// <summary>Generates a code and atomically reserves it in Redis for ReservationTtl. ~30.8 billion
    /// combinations at length 6, so collisions are exceedingly unlikely even under the 100k/year peak.</summary>
    public async Task<string> GenerateAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < settings.MaxGenerationRetries; attempt++)
        {
            var candidate = GenerateCandidate(settings.CodeLength);
            if (await reservationStore.TryReserveAsync(candidate, ReservationTtl, ct))
            {
                return candidate;
            }
        }

        throw new AppException(
            ErrorCodes.CodeGenerationFailed,
            $"Could not generate a unique short code after {settings.MaxGenerationRetries} attempts.");
    }

    private static string GenerateCandidate(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(0, Alphabet.Length)];
        }

        return new string(chars);
    }
}
