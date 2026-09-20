using FluentValidation;
using Nexus.ProjectManagement.Documents.Application.Dtos;

namespace Nexus.ProjectManagement.Documents.Application.Validators;

public sealed class UploadProjectDocumentRequestValidator : AbstractValidator<UploadProjectDocumentRequest>
{
    public UploadProjectDocumentRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
    }
}

public sealed class UpdateProjectDocumentRequestValidator : AbstractValidator<UpdateProjectDocumentRequest>
{
    public UpdateProjectDocumentRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
    }
}
