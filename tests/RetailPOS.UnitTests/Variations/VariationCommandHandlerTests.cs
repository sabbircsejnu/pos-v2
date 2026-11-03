using RetailPOS.Application.Variations;
using RetailPOS.Application.Variations.Abstractions;
using RetailPOS.Application.Variations.Commands;
using RetailPOS.Application.Variations.Queries;

namespace RetailPOS.Tests.Variations;

public sealed class VariationCommandHandlerTests
{
    [Fact]
    public async Task ReadQueries_ForwardFiltersAndPreserveMissingVariationError()
    {
        var variation = CreateVariation(1, "Color");
        var readStore = new FakeVariationReadStore { Result = [variation] };

        var list = await new GetVariationsQueryHandler(readStore)
            .Handle(new GetVariationsQuery(true), CancellationToken.None);
        Assert.Same(readStore.Result, list);
        Assert.True(readStore.IncludeInactive);

        var exception = await Assert.ThrowsAsync<Exception>(() =>
            new GetVariationByIdQueryHandler(readStore)
                .Handle(new GetVariationByIdQuery(99), CancellationToken.None));
        Assert.Equal("Variation not found", exception.Message);
    }

    [Fact]
    public async Task CreateAndUpdateCommands_PreserveDuplicateAndNotFoundValidationOrder()
    {
        var readStore = new FakeVariationReadStore();
        var writeStore = new FakeVariationWriteStore { NameExists = true };
        var createHandler = new CreateVariationCommandHandler(writeStore);

        var duplicate = await Assert.ThrowsAsync<Exception>(() => createHandler.Handle(
            new CreateVariationCommand(CreateInput("Color")),
            CancellationToken.None));
        Assert.Equal("Variation with name 'Color' already exists", duplicate.Message);
        Assert.False(writeStore.CreateCalled);

        var updateWriteStore = new FakeVariationWriteStore { NameExists = true };
        var updateHandler = new UpdateVariationCommandHandler(readStore, updateWriteStore);
        var missing = await Assert.ThrowsAsync<Exception>(() => updateHandler.Handle(
            new UpdateVariationCommand(99, CreateInput("Color")),
            CancellationToken.None));
        Assert.Equal("Variation not found", missing.Message);
        Assert.False(updateWriteStore.NameExistsCalled);

        readStore.Variation = CreateVariation(1, "Color");
        var duplicateUpdate = await Assert.ThrowsAsync<Exception>(() => updateHandler.Handle(
            new UpdateVariationCommand(1, CreateInput("Other")),
            CancellationToken.None));
        Assert.Equal("Variation with name 'Other' already exists", duplicateUpdate.Message);
        Assert.Equal(1, updateWriteStore.ExcludedVariationId);
    }

    [Fact]
    public async Task DeleteCommand_PreservesProductUseGuardAndReferences()
    {
        var readStore = new FakeVariationReadStore
        {
            Variation = CreateVariation(3, "Used"),
            Products =
            [
                new VariationProductReference(500, "Used Product")
            ]
        };
        var writeStore = new FakeVariationWriteStore();

        var exception = await Assert.ThrowsAsync<VariationInUseException>(() =>
            new DeleteVariationCommandHandler(readStore, writeStore)
                .Handle(new DeleteVariationCommand(3), CancellationToken.None));

        Assert.Equal(
            "Cannot delete variation 'Used' because it is used by 1 product(s).",
            exception.Message);
        Assert.Equal(new VariationProductReference(500, "Used Product"), Assert.Single(exception.Products));
        Assert.False(writeStore.DeleteCalled);
    }

    private static VariationReadModel CreateVariation(long id, string name) =>
        new(id, name, 1, true, false, []);

    private static VariationWriteModel CreateInput(string name) =>
        new(name, 1, true, false, []);

    private sealed class FakeVariationReadStore : IVariationReadStore
    {
        public IReadOnlyList<VariationReadModel> Result { get; init; } = [];
        public VariationReadModel? Variation { get; set; }
        public IReadOnlyList<VariationProductReference> Products { get; init; } = [];
        public bool IncludeInactive { get; private set; }

        public Task<IReadOnlyList<VariationReadModel>> GetAllAsync(
            bool includeInactive,
            CancellationToken cancellationToken)
        {
            IncludeInactive = includeInactive;
            return Task.FromResult(Result);
        }

        public Task<VariationReadModel?> GetByIdAsync(
            long variationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Variation?.Id == variationId ? Variation : null);

        public Task<VariationOptionReadModel?> GetOptionByIdAsync(
            long optionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<VariationOptionReadModel?>(null);

        public Task<IReadOnlyList<VariationProductReference>> GetProductsUsingVariationAsync(
            long variationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Products);
    }

    private sealed class FakeVariationWriteStore : IVariationWriteStore
    {
        public bool NameExists { get; init; }
        public bool NameExistsCalled { get; private set; }
        public bool CreateCalled { get; private set; }
        public bool DeleteCalled { get; private set; }
        public long? ExcludedVariationId { get; private set; }

        public Task<bool> NameExistsAsync(
            string name,
            long? excludeVariationId,
            CancellationToken cancellationToken)
        {
            NameExistsCalled = true;
            ExcludedVariationId = excludeVariationId;
            return Task.FromResult(NameExists);
        }

        public Task<VariationReadModel> CreateAsync(
            VariationWriteModel variation,
            CancellationToken cancellationToken)
        {
            CreateCalled = true;
            return Task.FromResult(CreateVariation(1, variation.Name));
        }

        public Task<VariationReadModel?> UpdateAsync(
            long variationId,
            VariationWriteModel variation,
            CancellationToken cancellationToken) =>
            Task.FromResult<VariationReadModel?>(CreateVariation(variationId, variation.Name));

        public Task DeleteAsync(long variationId, CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }

        public Task<VariationOptionReadModel> CreateOptionAsync(
            long variationId,
            VariationOptionWriteModel option,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VariationOptionReadModel(1, variationId, "Color", option.Name,
                option.PriceAdjustment, option.DisplayOrder, option.IsActive));

        public Task<VariationOptionReadModel?> UpdateOptionAsync(
            long optionId,
            VariationOptionWriteModel option,
            CancellationToken cancellationToken) =>
            Task.FromResult<VariationOptionReadModel?>(null);

        public Task DeleteOptionAsync(long optionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
