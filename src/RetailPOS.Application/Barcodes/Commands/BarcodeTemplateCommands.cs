using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Application.Barcodes.WriteModels;

namespace RetailPOS.Application.Barcodes.Commands;

public sealed record CreateBarcodeTemplateCommand(
    BarcodeTemplateWriteModel Template,
    long? UserId)
    : ICommand<BarcodeTemplateReadModel>;

public sealed class CreateBarcodeTemplateCommandHandler(
    IBarcodeTemplateWriteStore store,
    IBarcodeTemplateWriteScope writeScope)
    : IRequestHandler<CreateBarcodeTemplateCommand, BarcodeTemplateReadModel>
{
    public async Task<BarcodeTemplateReadModel> Handle(
        CreateBarcodeTemplateCommand request,
        CancellationToken cancellationToken)
    {
        ValidateTemplate(request.Template);
        var scope = await writeScope.GetAsync(cancellationToken);
        if (!scope.BusinessId.HasValue)
            throw new RetailPOS.Application.Authorization.BusinessContextRequiredException();

        return await store.CreateAsync(
            Normalize(request.Template),
            scope.BusinessId.Value,
            request.UserId,
            cancellationToken);
    }

    internal static void ValidateTemplate(BarcodeTemplateWriteModel template)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
            throw new InvalidOperationException("Template name is required");

        if (template.LabelWidthMm <= 0 || template.LabelHeightMm <= 0)
            throw new InvalidOperationException("Label width and height must be greater than zero");
    }

    internal static BarcodeTemplateWriteModel Normalize(BarcodeTemplateWriteModel template) =>
        template with
        {
            TemplateType = template.TemplateType.Trim().ToLowerInvariant(),
            PaperType = template.PaperType.Trim().ToLowerInvariant(),
            Fields = template.Fields.Select(field => field with
            {
                FieldKey = field.FieldKey.Trim().ToLowerInvariant(),
                FontWeight = field.FontWeight?.Trim(),
                Align = field.Align?.Trim().ToLowerInvariant()
            }).ToArray()
        };
}

public sealed record UpdateBarcodeTemplateCommand(
    long Id,
    BarcodeTemplateWriteModel Template,
    long? UserId)
    : ICommand<BarcodeTemplateReadModel>;

public sealed class UpdateBarcodeTemplateCommandHandler(
    IBarcodeTemplateWriteStore store,
    IBarcodeTemplateWriteScope writeScope)
    : IRequestHandler<UpdateBarcodeTemplateCommand, BarcodeTemplateReadModel>
{
    public async Task<BarcodeTemplateReadModel> Handle(
        UpdateBarcodeTemplateCommand request,
        CancellationToken cancellationToken)
    {
        CreateBarcodeTemplateCommandHandler.ValidateTemplate(request.Template);
        var scope = await writeScope.GetAsync(cancellationToken);
        return await store.UpdateAsync(
            request.Id,
            CreateBarcodeTemplateCommandHandler.Normalize(request.Template),
            scope,
            request.UserId,
            cancellationToken);
    }
}

public sealed record SetDefaultBarcodeTemplateCommand(long Id, long? UserId) : ICommand;

public sealed class SetDefaultBarcodeTemplateCommandHandler(
    IBarcodeTemplateWriteStore store,
    IBarcodeTemplateWriteScope writeScope)
    : IRequestHandler<SetDefaultBarcodeTemplateCommand>
{
    public async Task Handle(
        SetDefaultBarcodeTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var scope = await writeScope.GetAsync(cancellationToken);
        await store.SetDefaultAsync(request.Id, scope, request.UserId, cancellationToken);
    }
}

public sealed record DeleteBarcodeTemplateCommand(long Id) : ICommand;

public sealed class DeleteBarcodeTemplateCommandHandler(
    IBarcodeTemplateWriteStore store,
    IBarcodeTemplateWriteScope writeScope)
    : IRequestHandler<DeleteBarcodeTemplateCommand>
{
    public async Task Handle(
        DeleteBarcodeTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var scope = await writeScope.GetAsync(cancellationToken);
        await store.DeleteAsync(request.Id, scope, cancellationToken);
    }
}
