using RetailPOS.Application.Categories.Abstractions;
using RetailPOS.Application.Categories.Commands;
using RetailPOS.Application.Categories.Contracts;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Categories;

public sealed class CreateCategoryCommandValidatorTests
{
    private readonly CreateCategoryCommandValidator _validator = new();

    [Theory]
    [InlineData("", "Category name is required")]
    [InlineData(" ", "Category name is required")]
    [InlineData("x", "Category name must be between 2 and 100 characters")]
    public void Validate_InvalidName_ReturnsExistingMessage(string name, string expectedMessage)
    {
        var result = _validator.Validate(new CreateCategoryCommand(name, null, null, null, 0, true));

        Assert.Contains(result.Errors, failure =>
            failure.PropertyName == nameof(CreateCategoryCommand.Name)
            && failure.ErrorMessage == expectedMessage);
    }

    [Fact]
    public void Validate_InvalidOptionalFields_ReturnsExistingMessages()
    {
        var command = new CreateCategoryCommand(
            "Valid",
            new string('d', 501),
            null,
            new string('i', 501),
            10000,
            true);

        var result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure =>
            failure.PropertyName == nameof(CreateCategoryCommand.Description)
            && failure.ErrorMessage == "Description cannot exceed 500 characters");
        Assert.Contains(result.Errors, failure =>
            failure.PropertyName == nameof(CreateCategoryCommand.ImageUrl)
            && failure.ErrorMessage == "Image URL cannot exceed 500 characters");
        Assert.Contains(result.Errors, failure =>
            failure.PropertyName == nameof(CreateCategoryCommand.DisplayOrder)
            && failure.ErrorMessage == "Display order must be between 0 and 9999");
    }

    [Fact]
    public void Validate_ValidCommand_HasNoFailures()
    {
        var result = _validator.Validate(new CreateCategoryCommand(
            "Food",
            "Grocery items",
            3,
            "https://example.test/image.png",
            9999,
            true));

        Assert.Empty(result.Errors);
    }
}

public sealed class CreateCategoryCommandHandlerTests
{
    [Fact]
    public async Task Handle_DuplicateName_RejectsBeforeParentLookupOrCreate()
    {
        var catalog = new FakeCategoryCatalog { NameExists = true };
        var handler = new CreateCategoryCommandHandler(catalog);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CreateCategoryCommand("Books", null, 1, null, 0, true), CancellationToken.None));

        Assert.Equal("Category with name 'Books' already exists", exception.Message);
        Assert.Equal(0, catalog.ParentLookupCount);
        Assert.Null(catalog.CreatedCategory);
    }

    [Fact]
    public async Task Handle_MissingParent_RejectsWithoutCreating()
    {
        var catalog = new FakeCategoryCatalog();
        var handler = new CreateCategoryCommandHandler(catalog);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new CreateCategoryCommand("Books", null, 42, null, 0, true), CancellationToken.None));

        Assert.Equal("Parent category with ID 42 not found", exception.Message);
        Assert.Null(catalog.CreatedCategory);
    }

    [Fact]
    public async Task Handle_ValidRequest_PassesParentAndCancellationToCatalog()
    {
        var catalog = new FakeCategoryCatalog
        {
            Parent = new CategoryParent(5, "Root", 2)
        };
        var handler = new CreateCategoryCommandHandler(catalog);
        using var cancellation = new CancellationTokenSource();

        await handler.Handle(
            new CreateCategoryCommand("Books", "Reading", 5, "cover.png", 7, false),
            cancellation.Token);

        Assert.NotNull(catalog.CreatedCategory);
        Assert.Equal("Books", catalog.CreatedCategory.Name);
        Assert.Equal("Reading", catalog.CreatedCategory.Description);
        Assert.Equal(5, catalog.CreatedCategory.ParentCategoryId);
        Assert.Equal("cover.png", catalog.CreatedCategory.ImageUrl);
        Assert.Equal(7, catalog.CreatedCategory.DisplayOrder);
        Assert.False(catalog.CreatedCategory.IsActive);
        Assert.Equal(catalog.Parent, catalog.CreatedParent);
        Assert.Equal(cancellation.Token, catalog.LastCancellationToken);
    }

    private sealed class FakeCategoryCatalog : ICategoryCatalog
    {
        public bool NameExists { get; init; }
        public CategoryParent? Parent { get; init; }
        public int ParentLookupCount { get; private set; }
        public Category? CreatedCategory { get; private set; }
        public CategoryParent? CreatedParent { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<IReadOnlyList<CategoryView>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CategoryView>>([]);

        public Task<bool> NameExistsAsync(
            string name,
            long? excludingId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(NameExists);
        }

        public Task<CategoryParent?> GetParentAsync(long id, CancellationToken cancellationToken)
        {
            ParentLookupCount++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Parent);
        }

        public Task<Category?> GetForMutationAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult<Category?>(null);

        public Task<IReadOnlySet<long>> GetDescendantIdsAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());

        public Task<bool> HasChildrenAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasProductsAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<CategoryView?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult<CategoryView?>(null);

        public Task<CategoryView> CreateAsync(
            Category category,
            CategoryParent? parent,
            CancellationToken cancellationToken)
        {
            CreatedCategory = category;
            CreatedParent = parent;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(new CategoryView(
                10,
                category.Name,
                category.Description,
                category.ParentCategoryId,
                parent?.Name,
                category.ImageUrl,
                category.DisplayOrder,
                category.IsActive,
                DateTime.UtcNow,
                DateTime.UtcNow,
                parent is null ? 0 : parent.Level + 1,
                0,
                0));
        }

        public Task<CategoryView> UpdateAsync(Category category, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(long id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
