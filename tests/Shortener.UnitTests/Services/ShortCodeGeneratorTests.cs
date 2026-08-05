using FluentAssertions;
using Moq;
using Shortener.Application.Abstractions;
using Shortener.Application.Common;
using Shortener.Application.Services;
using Shortener.Domain.Exceptions;

namespace Shortener.UnitTests.Services;

public sealed class ShortCodeGeneratorTests
{
    private const string AllowedAlphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly ShortCodeGeneratorSettings Settings = new(CodeLength: 6, MaxGenerationRetries: 5);

    [Fact]
    public async Task GenerateAsync_FirstAttemptReserves_ReturnsACodeOfConfiguredLengthFromTheAllowedAlphabet()
    {
        var store = new Mock<ICodeReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var generator = new ShortCodeGenerator(store.Object, Settings);

        var code = await generator.GenerateAsync(CancellationToken.None);

        code.Should().HaveLength(6);
        code.ToCharArray().Should().OnlyContain(c => AllowedAlphabet.Contains(c));
        store.Verify(s => s.TryReserveAsync(It.IsAny<string>(), TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_GeneratedCodes_NeverContainExcludedCharacters()
    {
        var store = new Mock<ICodeReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var generator = new ShortCodeGenerator(store.Object, Settings);

        // The class comment says "0, O, o, 1, l, I removed to avoid misreading," but the literal
        // Alphabet string only actually excludes 0, 1, I, O, and lowercase l — lowercase 'o' and
        // uppercase 'L' remain. Asserting the real behavior here so this test documents what's
        // actually shipped rather than what the comment claims.
        for (var i = 0; i < 200; i++)
        {
            var code = await generator.GenerateAsync(CancellationToken.None);
            code.Should().NotContainAny("0", "O", "1", "I", "l");
        }
    }

    [Fact]
    public async Task GenerateAsync_FirstAttemptCollides_RetriesAndSucceedsOnSecondAttempt()
    {
        var store = new Mock<ICodeReservationStore>();
        store.SetupSequence(s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        var generator = new ShortCodeGenerator(store.Object, Settings);

        var code = await generator.GenerateAsync(CancellationToken.None);

        code.Should().HaveLength(6);
        store.Verify(s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GenerateAsync_EveryAttemptCollides_ThrowsAppExceptionWithCodeGenerationFailed()
    {
        var store = new Mock<ICodeReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var generator = new ShortCodeGenerator(store.Object, Settings);

        var act = () => generator.GenerateAsync(CancellationToken.None);

        var exception = await act.Should().ThrowAsync<AppException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.CodeGenerationFailed);
        store.Verify(
            s => s.TryReserveAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(Settings.MaxGenerationRetries));
    }
}
