using MediatR;

namespace RetailPOS.Application.Abstractions.Messaging;

/// <summary>Marks a read-only information request.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}
