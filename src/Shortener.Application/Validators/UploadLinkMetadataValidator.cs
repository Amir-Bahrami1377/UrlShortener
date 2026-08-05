using FluentValidation;
using Shortener.Application.Contracts;

namespace Shortener.Application.Validators;

public sealed class UploadLinkMetadataValidator : AbstractValidator<UploadLinkMetadata>
{
    public UploadLinkMetadataValidator()
    {
        RuleFor(x => x.Shop).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Shod).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Radif).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ReportId).GreaterThan(0);
        RuleFor(x => x.ReportName).NotEmpty().MaximumLength(300);
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Matches(@"^09\d{9}$")
            .WithMessage("شماره موبایل باید به‌صورت ۰۹xxxxxxxxx باشد.");
        RuleFor(x => x.ClientRequestId).MaximumLength(64);
        RuleFor(x => x.BatchTag).MaximumLength(64);
    }
}
