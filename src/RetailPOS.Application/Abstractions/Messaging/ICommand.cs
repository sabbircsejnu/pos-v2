using MediatR;

namespace RetailPOS.Application.Abstractions.Messaging;

/// <summary>Marks a state-changing request that does not return a value.</summary>
public interface ICommand : IRequest
{
}

/// <summary>Marks a state-changing request with a response.</summary>
public interface ICommand<out TResponse> : IRequest<TResponse>
{
}
