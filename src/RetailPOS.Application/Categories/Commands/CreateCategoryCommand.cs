using FluentValidation;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Categories.Abstractions;
using RetailPOS.Application.Categories.Contracts;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Categories.Commands;

public sealed record CreateCategoryCommand(
    string Name,
    string? Description,
    long? ParentCategoryId,
    string? ImageUrl,
    int DisplayOrder,
    bool IsActive) : ICommand<CategoryView>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Category name is required")
            .Length(2, 100)
            .WithMessage("Category name must be between 2 and 100 characters");

        RuleFor(command => command.Description)
            .MaximumLength(500)
            .WithMessage("Description cannot exceed 500 characters");

        RuleFor(command => command.ImageUrl)
            .MaximumLength(500)
            .WithMessage("Image URL cannot exceed 500 characters");

        RuleFor(command => command.DisplayOrder)
            .InclusiveBetween(0, 9999)
            .WithMessage("Display order must be between 0 and 9999");
    }
}

public sealed class CreateCategoryCommandHandler(ICategoryCatalog catalog)
    : IRequestHandler<CreateCategoryCommand, CategoryView>
{
    public async Task<CategoryView> Handle(
        CreateCategoryCommand request,
        CancellationToken cancellationToken)
    {
        if (await catalog.NameExistsAsync(request.Name, null, cancellationToken))
        {
            throw new InvalidOperationException($"Category with name '{request.Name}' already exists");
        }

        CategoryParent? parent = null;
        if (request.ParentCategoryId is long parentId)
        {
            parent = await catalog.GetParentAsync(parentId, cancellationToken);
            if (parent is null)
            {
                throw new KeyNotFoundException($"Parent category with ID {parentId} not found");
            }
        }

        var category = new Category
        {
            Name = request.Name,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            ImageUrl = request.ImageUrl,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive
        };

        return await catalog.CreateAsync(category, parent, cancellationToken);
    }
}
