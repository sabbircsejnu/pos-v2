using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Behaviors;

namespace RetailPOS.UnitTests.Application;

public sealed class ApplicationFoundationTests
{
    [Fact]
    public void AddApplication_RegistersTransientMediatRServices_AndIsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddApplication();
        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        var firstSender = provider.GetRequiredService<ISender>();
        var secondSender = provider.GetRequiredService<ISender>();
        var firstMediator = provider.GetRequiredService<IMediator>();
        var secondMediator = provider.GetRequiredService<IMediator>();
        var validationBehaviors = services.Where(descriptor =>
            descriptor.ServiceType == typeof(IPipelineBehavior<,>)
            && descriptor.ImplementationType == typeof(ValidationBehavior<,>));

        Assert.NotSame(firstSender, secondSender);
        Assert.NotSame(firstMediator, secondMediator);
        Assert.Single(validationBehaviors);
    }

    [Fact]
    public async Task ValidationBehavior_WithoutValidators_InvokesNext()
    {
        var behavior = CreateBehavior();
        var nextCalled = false;

        var result = await behavior.Handle(
            new ProbeQuery("value"),
            () =>
            {
                nextCalled = true;
                return Task.FromResult("handled");
            },
            CancellationToken.None);

        Assert.Equal("handled", result);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ValidationBehavior_WithSuccessfulValidator_InvokesNext()
    {
        var behavior = CreateBehavior(new RequiredValueValidator());
        var nextCalled = false;

        var result = await behavior.Handle(
            new ProbeQuery("present"),
            () =>
            {
                nextCalled = true;
                return Task.FromResult("handled");
            },
            CancellationToken.None);

        Assert.Equal("handled", result);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ValidationBehavior_WithFailure_ThrowsStructuredValidationExceptionWithoutInvokingNext()
    {
        var behavior = CreateBehavior(new RequiredValueValidator());
        var nextCalled = false;

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(
            new ProbeQuery(string.Empty),
            () =>
            {
                nextCalled = true;
                return Task.FromResult("handled");
            },
            CancellationToken.None));

        var failure = Assert.Single(exception.Errors);
        Assert.Equal(nameof(ProbeQuery.Value), failure.PropertyName);
        Assert.Equal("A value is required.", failure.ErrorMessage);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task ValidationBehavior_AggregatesAndSortsDistinctFailures()
    {
        var behavior = CreateBehavior(
            new FirstFailureValidator(),
            new DuplicateFailureValidator(),
            new SecondFailureValidator());

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(
            new ProbeQuery("value"),
            () => Task.FromResult("not-called"),
            CancellationToken.None));

        Assert.Collection(
            exception.Errors,
            failure =>
            {
                Assert.Equal(nameof(ProbeQuery.Value), failure.PropertyName);
                Assert.Equal("First failure.", failure.ErrorMessage);
            },
            failure =>
            {
                Assert.Equal(nameof(ProbeQuery.Value), failure.PropertyName);
                Assert.Equal("Second failure.", failure.ErrorMessage);
            });
    }

    [Fact]
    public async Task ValidationBehavior_PropagatesCancellationTokenToValidatorsAndNext()
    {
        var validator = new CancellationTrackingValidator();
        var handler = new CancellationTrackingHandler();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IValidator<ProbeQuery>>(validator);
        services.AddSingleton<IRequestHandler<ProbeQuery, string>>(handler);
        using var provider = services.BuildServiceProvider();
        using var source = new CancellationTokenSource();

        var result = await provider.GetRequiredService<ISender>()
            .Send(new ProbeQuery("value"), source.Token);

        Assert.Equal("handled", result);
        Assert.Equal(source.Token, validator.ReceivedToken);
        Assert.Equal(source.Token, handler.ReceivedToken);
    }

    [Fact]
    public async Task ValidationBehavior_DoesNotSwallowHandlerExceptions()
    {
        var behavior = CreateBehavior();
        var expected = new InvalidOperationException("handler failed");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new ProbeQuery("value"),
            () => Task.FromException<string>(expected),
            CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void CommandAndQueryContracts_DeriveFromTheExpectedMediatRRequests()
    {
        Assert.True(typeof(IRequest).IsAssignableFrom(typeof(NoResponseCommand)));
        Assert.True(typeof(IRequest<string>).IsAssignableFrom(typeof(TextCommand)));
        Assert.True(typeof(IRequest<string>).IsAssignableFrom(typeof(TextQuery)));
    }

    private static ValidationBehavior<ProbeQuery, string> CreateBehavior(
        params IValidator<ProbeQuery>[] validators) => new(validators);

    private sealed record ProbeQuery(string Value) : IQuery<string>;

    private sealed class NoResponseCommand : ICommand
    {
    }

    private sealed class TextCommand : ICommand<string>
    {
    }

    private sealed class TextQuery : IQuery<string>
    {
    }

    private sealed class RequiredValueValidator : AbstractValidator<ProbeQuery>
    {
        public RequiredValueValidator()
        {
            RuleFor(request => request.Value)
                .NotEmpty()
                .WithMessage("A value is required.");
        }
    }

    private sealed class FirstFailureValidator : AbstractValidator<ProbeQuery>
    {
        public FirstFailureValidator()
        {
            RuleFor(request => request.Value)
                .Must(_ => false)
                .WithMessage("First failure.");
        }
    }

    private sealed class DuplicateFailureValidator : AbstractValidator<ProbeQuery>
    {
        public DuplicateFailureValidator()
        {
            RuleFor(request => request.Value)
                .Must(_ => false)
                .WithMessage("First failure.");
        }
    }

    private sealed class SecondFailureValidator : AbstractValidator<ProbeQuery>
    {
        public SecondFailureValidator()
        {
            RuleFor(request => request.Value)
                .Must(_ => false)
                .WithMessage("Second failure.");
        }
    }

    private sealed class CancellationTrackingValidator : AbstractValidator<ProbeQuery>
    {
        public CancellationToken ReceivedToken { get; private set; }

        public CancellationTrackingValidator()
        {
            RuleFor(request => request.Value)
                .MustAsync((_, cancellationToken) =>
                {
                    ReceivedToken = cancellationToken;
                    return Task.FromResult(true);
                });
        }
    }

    private sealed class CancellationTrackingHandler : IRequestHandler<ProbeQuery, string>
    {
        public CancellationToken ReceivedToken { get; private set; }

        public Task<string> Handle(ProbeQuery request, CancellationToken cancellationToken)
        {
            ReceivedToken = cancellationToken;
            return Task.FromResult("handled");
        }
    }
}
