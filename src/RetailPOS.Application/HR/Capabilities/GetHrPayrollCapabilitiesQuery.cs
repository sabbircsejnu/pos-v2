using MediatR;
using RetailPOS.Application.Abstractions.Messaging;

namespace RetailPOS.Application.HR.Capabilities;

public sealed record GetHrPayrollCapabilitiesQuery : IQuery<HrPayrollCapabilities>;

public sealed class GetHrPayrollCapabilitiesQueryHandler(IHrPayrollCapabilityService capabilities)
    : IRequestHandler<GetHrPayrollCapabilitiesQuery, HrPayrollCapabilities>
{
    public Task<HrPayrollCapabilities> Handle(
        GetHrPayrollCapabilitiesQuery request, CancellationToken cancellationToken) =>
        capabilities.GetAsync(cancellationToken);
}
