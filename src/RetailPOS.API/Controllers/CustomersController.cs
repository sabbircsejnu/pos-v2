using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Customer;
using RetailPOS.API.Models;
using RetailPOS.Application.Customers.Queries;
using RetailPOS.Application.Customers.Commands;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = "customers.view")]
public class CustomersController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IConfiguration _configuration;

    public CustomersController(ISender sender, IConfiguration configuration)
    {
        _sender = sender;
        _configuration = configuration;
    }

    /// <summary>List customers with optional search and pagination</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CustomerListDto>>> GetAll(
        [FromQuery] CustomerSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchCustomersQuery(searchDto.Query, searchDto.PageNumber, searchDto.PageSize),
            cancellationToken);
        var response = new CustomerListDto
        {
            Customers = result.Customers.Select(MapReadModel).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        };
        return Ok(ApiResponse<CustomerListDto>.SuccessResponse(response));
    }

    /// <summary>Quick search customers by name/phone/email for POS autocomplete</summary>
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<IEnumerable<CustomerDto>>>> Search(
        [FromQuery] string q,
        CancellationToken cancellationToken)
    {
        var customers = await _sender.Send(new QuickSearchCustomersQuery(q), cancellationToken);
        return Ok(ApiResponse<IEnumerable<CustomerDto>>.SuccessResponse(customers.Select(MapReadModel)));
    }

    /// <summary>Get a customer by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var customer = await _sender.Send(new GetCustomerByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(MapReadModel(customer)));
    }

    /// <summary>Create a new customer</summary>
    [HttpPost]
    [Authorize(Policy = "customers.create")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> Create(
        [FromBody] CreateCustomerDto dto,
        CancellationToken cancellationToken)
    {
        var customer = await _sender.Send(
            new CreateCustomerCommand(dto.Name, dto.Phone, dto.Email),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = customer.Id },
            ApiResponse<CustomerDto>.SuccessResponse(MapReadModel(customer), "Customer created successfully"));
    }

    /// <summary>Update an existing customer</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "customers.edit")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> Update(
        long id,
        [FromBody] UpdateCustomerDto dto,
        CancellationToken cancellationToken)
    {
        var customer = await _sender.Send(
            new UpdateCustomerCommand(id, dto.Name, dto.Phone, dto.Email),
            cancellationToken);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(
            MapReadModel(customer),
            "Customer updated successfully"));
    }

    /// <summary>Delete a customer</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "customers.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(
        long id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteCustomerCommand(id), cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Customer deleted successfully"));
    }

    /// <summary>
    /// Legacy integer-balance mutation. Disabled unless FeatureFlags:LegacyLoyaltyPointsMutationEnabled is true and the
    /// caller can edit customers; the Loyalty module replaces it (SRS FR-SEC-02).
    /// </summary>
    [HttpPost("{id}/add-points")]
    [Authorize(Policy = "customers.edit")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> AddPoints(
        long id,
        [FromQuery] int points,
        CancellationToken cancellationToken)
    {
        if (!LegacyLoyaltyMutationEnabled)
            return LegacyLoyaltyDisabled();

        var customer = await _sender.Send(
            new AddCustomerLoyaltyPointsCommand(id, points),
            cancellationToken);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(
            MapReadModel(customer),
            $"{points} loyalty points added"));
    }

    /// <summary>Legacy integer-balance mutation; see <see cref="AddPoints"/>.</summary>
    [HttpPost("{id}/redeem-points")]
    [Authorize(Policy = "customers.edit")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> RedeemPoints(
        long id,
        [FromQuery] int points,
        CancellationToken cancellationToken)
    {
        if (!LegacyLoyaltyMutationEnabled)
            return LegacyLoyaltyDisabled();

        var customer = await _sender.Send(
            new RedeemCustomerLoyaltyPointsCommand(id, points),
            cancellationToken);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(
            MapReadModel(customer),
            $"{points} loyalty points redeemed"));
    }

    private bool LegacyLoyaltyMutationEnabled =>
        _configuration.GetValue<bool>("FeatureFlags:LegacyLoyaltyPointsMutationEnabled");

    /// <summary>Tells the UI whether legacy point mutation is available so it can hide actions that would always be refused.</summary>
    [HttpGet("loyalty-capabilities")]
    public ActionResult<ApiResponse<LoyaltyCapabilitiesDto>> GetLoyaltyCapabilities() =>
        Ok(ApiResponse<LoyaltyCapabilitiesDto>.SuccessResponse(
            new LoyaltyCapabilitiesDto { LegacyPointsMutationEnabled = LegacyLoyaltyMutationEnabled }));

    private ObjectResult LegacyLoyaltyDisabled() => StatusCode(
        StatusCodes.Status403Forbidden,
        ApiResponse<CustomerDto>.ErrorResponse(
            "Manual loyalty point changes are disabled. Loyalty is managed by the Loyalty module."));

    private static CustomerDto MapReadModel(RetailPOS.Application.Customers.CustomerReadModel customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        Phone = customer.Phone,
        Email = customer.Email,
        LoyaltyPoints = customer.LoyaltyPoints,
        CreatedAt = customer.CreatedAt,
        TotalPurchases = customer.TotalPurchases
    };
}
