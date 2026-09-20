using FluentValidation;
using Nexus.Knowledge.Application.Dtos;

namespace Nexus.Knowledge.Application.Validators;

public sealed class UploadKnowledgeDocumentRequestValidator : AbstractValidator<UploadKnowledgeDocumentRequest>
{
    public UploadKnowledgeDocumentRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
    }
}

public sealed class UpdateKnowledgeDocumentRequestValidator : AbstractValidator<UpdateKnowledgeDocumentRequest>
{
    public UpdateKnowledgeDocumentRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}
